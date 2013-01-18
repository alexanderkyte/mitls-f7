﻿module AEAD

open Bytes

open TLSConstants
open TLSInfo
open Error
open Range

type cipher = bytes

(***** keying *****) 

type AEADKey =
    | MtE of MAC.key * ENC.state
    | MACOnly of MAC.key
(*  | GCM of AENC.state  *)

let GEN e =
    let si = epochSI(e) in
    let cs = si.cipher_suite in
    match cs with
    | x when isOnlyMACCipherSuite x ->
        let mk = MAC.GEN e
        (MACOnly(mk), MACOnly(mk))
    | _ ->
        let mk = MAC.GEN e in
        let (ek,dk) = ENC.GEN e in
        (MtE(mk,ek),MtE(mk,dk))

let COERCE e b =
    // precondition: b is of the right length. No runtime checks here.
    let si = epochSI(e) in
    let cs = si.cipher_suite in
    let pv = si.protocol_version in
    if isOnlyMACCipherSuite cs then 
      let mk = MAC.COERCE e b in
      MACOnly(mk)
    else 
    if isAEADCipherSuite cs then
      let macalg = macAlg_of_ciphersuite cs pv in
      let encalg = encAlg_of_ciphersuite cs pv in
      let macKeySize = macKeySize macalg in
      let encKeySize = encKeySize encalg in
      let (mkb,rest) = split b macKeySize in
      let (ekb,ivb) = split rest encKeySize in
      let mk = MAC.COERCE e mkb in
      let ek = ENC.COERCE e ekb ivb in
      MtE(mk,ek)
    else 
      unexpectedError "[COERCE] invoked on wrong ciphersuite"

let LEAK e k =
    match k with
    | MACOnly(mk) -> MAC.LEAK e mk
    | MtE(mk,ek) ->
        let (k,iv) = ENC.LEAK e ek in
        MAC.LEAK e mk @| k @| iv

(***** authenticated encryption *****)

let encrypt' e key data rg plain =
    let si = epochSI(e) in
    let cs = si.cipher_suite in
    let pv = si.protocol_version in
    match (cs,key) with
    | (x, MtE (ka,ke)) when isAEADCipherSuite x ->
        let encAlg = encAlg_of_ciphersuite cs pv in
        match encAlg with
        | Stream_RC4_128 -> // stream cipher
            let tag   = Encode.mac e ka data rg plain in
            let (l,h) = rg in
            if l <> h then
                unexpectedError "[encrypt'] given an invalid input range"
            else
                let tlen  = targetLength e rg in
                let encoded  = Encode.encodeNoPad e tlen rg data plain tag in
                let (ke,res) = ENC.ENC e ke tlen encoded 
                (MtE(ka,ke),res)
        | CBC_Stale(_) | CBC_Fresh(_) -> // block cipher
            let tag  = Encode.mac e ka data rg plain in
            let tlen = targetLength e rg in
            let encoded  = Encode.encode e tlen rg data plain tag in
            let (ke,res) = ENC.ENC e ke tlen encoded 
            (MtE(ka,ke),res)
    | (x,MACOnly (ka)) when isOnlyMACCipherSuite x ->
        let tag = Encode.mac e ka data rg plain in
        let (l,h) = rg in
        if l <> h then
            unexpectedError "[encrypt'] given an invalid input range"
        else
            let tlen  = targetLength e rg in
            let encoded = Encode.encodeNoPad e tlen rg data plain tag in
            let r = Encode.repr e tlen encoded in
            (key,r)
//  | GCM (k) -> ... 
    | (_,_) -> unexpectedError "[encrypt'] incompatible ciphersuite-key given."
        
let mteKey (e:epoch) ka ke = MtE(ka,ke)

let decrypt' e key data cipher =
    let cl = length cipher in
    // by typing, we know that cl <= max_TLSCipher_fragment_length
    let si = epochSI(e) in
    let cs = si.cipher_suite in
    let pv = si.protocol_version in
    let macSize = macSize (macAlg_of_ciphersuite cs pv) in
    match (cs,key) with
    | (x, MtE (ka,ke)) when isAEADCipherSuite x ->
        let encAlg = encAlg_of_ciphersuite cs pv in
        match encAlg with
        | Stream_RC4_128 -> // stream cipher
            if cl < macSize then
                (*@ It is safe to return early, because we are branching
                    on public data known to the attacker *)
                let reason = perror __SOURCE_FILE__ __LINE__ "" in Error(AD_bad_record_mac, reason)
            else
                let (ke,encoded) = ENC.DEC e ke cipher in
                let nk = mteKey e ka ke in
                let rg = cipherRangeClass e cl in
                let parsed = Encode.decodeNoPad e data rg cl encoded in
                match Encode.verify e ka data rg parsed with
                | Error(x,y) -> Error(x,y)
                | Correct(plain) -> correct(nk,rg,plain)
        | CBC_Stale(alg) | CBC_Fresh(alg) -> // block cipher
            let ivL = ivLength e in
            let blockSize = blockSize alg in
            if (cl - ivL < macSize + 1) || (cl % blockSize <> 0) then
                (*@ It is safe to return early, because we are branching
                    on public data known to the attacker *)
                let reason = perror __SOURCE_FILE__ __LINE__ "" in Error(AD_bad_record_mac, reason)
            else
                let (ke,encoded) = ENC.DEC e ke cipher in
                let nk = mteKey e ka ke in
                let rg = cipherRangeClass e cl in
                let parsed = Encode.decode e data rg cl encoded in
                match Encode.verify e ka data rg parsed with
                | Error(x,y) -> Error(x,y)
                | Correct(plain) -> correct (nk,rg,plain)
    | (x,MACOnly (ka)) when isOnlyMACCipherSuite x ->
        if cl < macSize then
            let reason = perror __SOURCE_FILE__ __LINE__ "" in Error(AD_bad_record_mac, reason)
        else
            let rg = cipherRangeClass e cl in
            let encoded = Encode.plain e cl cipher in
            let parsed  = Encode.decodeNoPad e data rg cl encoded in
            match Encode.verify e ka data rg parsed with
            | Error(x,y) -> Error(x,y)
            | Correct(plain) -> correct (key,rg,plain)
//  | GCM (GCMKey) -> ... 
    | (_,_) -> unexpectedError "[decrypt'] incompatible ciphersuite-key given."

#if ideal

type preds = | ENCrypted of epoch * AEADPlain.adata * range * AEADPlain.plain * cipher

type entry = epoch * AEADPlain.adata * range * AEADPlain.plain * ENC.cipher
let log = ref ([]: entry list) // the semantics of CTXT

let rec cmem (e:epoch) (ad:AEADPlain.adata) (c:ENC.cipher) (xs: entry list) = failwith "verify"
//  match xs with
//  | (e',ad',r,p,c')::_ when e=e' && ad=ad' && c=c' -> let x = (r,p) in Some x
//  | _::xs                  -> cmem e ad c xs 
//  | []                     -> None

let safe (e:epoch) = failwith "todo"

#endif

let encrypt e key data rg plain = 
  let (key,cipher) = encrypt' e key data rg plain in
  #if ideal
  let p' = AEADPlain.widen e data rg plain in
  let rg' = rangeClass e rg in
  Pi.assume(ENCrypted(e,data,rg',p',cipher));
  log := (e,data,rg',p',cipher)::!log;
  #endif
  (key,cipher)

let decrypt e (key: AEADKey) data (cipher: bytes) =  
  #if ideal
  if safe e then
    match cmem e data cipher !log with
    // | Some _ -> (* 1 *)
    //   decrypt' e key data cipher
        
    | Some x -> (* 2 *)
      let (r,p) = x
      correct (key,r,p)

    | None   -> Error(AD_bad_record_mac, "")  
  else
  #endif 
      decrypt' e key data cipher
