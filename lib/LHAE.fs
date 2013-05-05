﻿module LHAE

open Bytes

open TLSConstants
open TLSInfo
open Error
open TLSError
open Range

type cipher = bytes

(***** keying *****) 

type LHAEKey =
    | MtEK of MAC.key * ENC.state
    | MACOnlyK of MAC.key
(*  | GCM of AENC.state  *)

let GEN e =
    let si = epochSI(e) in
    let authEnc = aeAlg si.cipher_suite si.protocol_version in
    match authEnc with
    | MACOnly _ ->
        let mk = MAC.GEN e
        (MACOnlyK(mk), MACOnlyK(mk))
    | MtE(_,_) ->
        let mk = MAC.GEN e in
        let (ek,dk) = ENC.GEN e in
        (MtEK(mk,ek),MtEK(mk,dk))
    | AEAD (_,_) -> unexpected "[GEN] invoked on unsupported ciphersuite"

let COERCE e b =
    // precondition: b is of the right length, so no need for a runtime checks here.
    let si = epochSI(e) in
    let cs = si.cipher_suite in
    let pv = si.protocol_version in
    let authEnc = aeAlg cs pv in
    match authEnc with
    | MACOnly _ -> 
      let mk = MAC.COERCE e b in
      MACOnlyK(mk)
    | MtE(encalg,macalg) ->
      let macKeySize = macKeySize macalg in
      let encKeySize = encKeySize encalg in
      let (mkb,rest) = split b macKeySize in
      let (ekb,ivb) = split rest encKeySize in
      let mk = MAC.COERCE e mkb in
      let ek = ENC.COERCE e ekb ivb in
      MtEK(mk,ek)
    | AEAD (_,_) -> 
      unexpected "[COERCE] invoked on wrong ciphersuite"

let LEAK e k =
    match k with
    | MACOnlyK(mk) -> MAC.LEAK e mk
    | MtEK(mk,ek) ->
        let (k,iv) = ENC.LEAK e ek in
        MAC.LEAK e mk @| k @| iv

(***** authenticated encryption *****)

let encrypt' e key data rg plain =
    let si = epochSI(e) in
    let cs = si.cipher_suite in
    let pv = si.protocol_version in
    let authEnc = aeAlg cs pv in
    match (authEnc,key) with
    | (MtE(encAlg,_), MtEK (ka,ke)) ->
        match encAlg with
        | Stream_RC4_128 -> // stream cipher
            let plain   = Encode.mac e ka data rg plain in
            let (l,h) = rg in
            if l <> h then
                unexpected "[encrypt'] given an invalid input range"
            else
                let (ke,res) = ENC.ENC e ke data rg plain 
                (MtEK(ka,ke),res)
        | CBC_Stale(_) | CBC_Fresh(_) -> // block cipher
            let plain  = Encode.mac e ka data rg plain in
            let (ke,res) = ENC.ENC e ke data rg plain 
            (MtEK(ka,ke),res)
    | (MACOnly _, MACOnlyK (ka)) ->
        let plain = Encode.mac e ka data rg plain in
        let (l,h) = rg in
        if l <> h then
            unexpected "[encrypt'] given an invalid input range"
        else
            let r = Encode.repr e data rg plain in
            (key,r)
//  | GCM (k) -> ... 
    | (_,_) -> unexpected "[encrypt'] incompatible ciphersuite-key given."
        
let mteKey (e:epoch) ka ke = MtEK(ka,ke)

let decrypt' e key data cipher =
    let cl = length cipher in
    // by typing, we know that cl <= max_TLSCipher_fragment_length
    let si = epochSI(e) in
    let cs = si.cipher_suite in
    let pv = si.protocol_version in
    let authEnc = aeAlg cs pv in
    match (authEnc,key) with
    | (MtE(encAlg,macAlg), MtEK (ka,ke)) ->
        let macSize = macSize macAlg in
        match encAlg with
        | Stream_RC4_128 -> // stream cipher
            if cl < macSize then
                (*@ It is safe to return early, because we are branching
                    on public data known to the attacker *)
                let reason = perror __SOURCE_FILE__ __LINE__ "" in Error(AD_bad_record_mac, reason)
            else
                let rg = cipherRangeClass e cl in
                let (ke,plain) = ENC.DEC e ke data cipher in
                let nk = mteKey e ka ke in
                match Encode.verify e ka data rg plain with
                | Error z -> Error z
                | Correct(aeplain) -> correct(nk,rg,aeplain)
        | CBC_Stale(alg) | CBC_Fresh(alg) -> // block cipher
            let ivL = ivSize e in
            let blockSize = blockSize alg in
            if (cl - ivL < macSize + 1) || (cl % blockSize <> 0) then
                (*@ It is safe to return early, because we are branching
                    on public data known to the attacker *)
                let reason = perror __SOURCE_FILE__ __LINE__ "" in Error(AD_bad_record_mac, reason)
            else
                let rg = cipherRangeClass e cl in
                let (ke,plain) = ENC.DEC e ke data cipher in
                let nk = mteKey e ka ke in
                match Encode.verify e ka data rg plain with
                | Error z -> Error z
                | Correct(aeplain) -> correct (nk,rg,aeplain)
    | (MACOnly macAlg ,MACOnlyK (ka)) ->
        let macSize = macSize macAlg in
        if cl < macSize then
            let reason = perror __SOURCE_FILE__ __LINE__ "" in Error(AD_bad_record_mac, reason)
        else
            let rg = cipherRangeClass e cl in
            let plain = Encode.plain e data cl cipher in
            match Encode.verify e ka data rg plain with
            | Error(z) -> Error(z)
            | Correct(aeplain) -> correct (key,rg,aeplain)
//  | GCM (GCMKey) -> ... 
    | (_,_) -> unexpected "[decrypt'] incompatible ciphersuite-key given."

#if ideal

type preds = | ENCrypted of epoch * LHAEPlain.adata * range * LHAEPlain.plain * cipher

type entry = epoch * LHAEPlain.adata * range * LHAEPlain.plain * ENC.cipher
let log = ref ([]: entry list) // for defining the ideal functionality for CTXT

let rec cmem (e:epoch) (ad:LHAEPlain.adata) (c:ENC.cipher) (xs: entry list) = 
#if verify
  failwith "specification only"
#else
  match xs with
  | (e',ad',r,p,c')::_ when e=e' && ad=ad' && c=c' -> let x = (r,p) in Some x
  | _::xs                  -> cmem e ad c xs 
  | []                     -> None
#endif

#endif

let encrypt e key data rg plain = 
  let (key,cipher) = encrypt' e key data rg plain in
  #if ideal
  (* CF we do not log in all cases, as we do not have ENCrypted for MAC-only suites *)
  if safe e then
    log := (e,data,rg,plain,cipher)::!log
  else ()
  #endif
  #if ideal_F
  if safe e then
    log := (e,data,rg,plain,cipher)::!log
  else ()
  #endif
  (key,cipher)

let decrypt e (key: LHAEKey) data (cipher: bytes) = 
  let err = (AD_bad_record_mac,"") in
  #if ideal_F
  if safe e then
    match cmem e data cipher !log with
    | Some _ -> decrypt' e key data cipher
    | None   -> Error err
  else
  #endif 
  #if ideal
  if safe e then
    match cmem e data cipher !log with
    | Some x -> 
       let (r,p) = x in
       let p' = LHAEPlain.widen e data r p in
       let tlen = length cipher in
       let rg' = cipherRangeClass e tlen in
       correct (key,rg',p')
    | None   -> Error err
  else
  #endif 
      decrypt' e key data cipher
