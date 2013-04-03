﻿module Sig 

open Bytes
open TLSConstants
open CoreSig

(* ------------------------------------------------------------------------ *)
//MK: now defined in TLSConstants type alg   = sigAlg * hashAlg
type alg = sigHashAlg

type text = bytes
type sigv = bytes 

(* ------------------------------------------------------------------------ *)
type skey = { skey : sigskey * hashAlg }
type pkey = { pkey : sigpkey * hashAlg }

// MK let create_skey (h : hashAlg) (p : CoreSig.sigskey) = { skey = (p, h) }


// MK let repr_of_skey { skey = skey } = skey
// MK let repr_of_pkey { pkey = pkey } = pkey

let sigalg_of_skeyparams = function
    | CoreSig.SK_RSA _ -> SA_RSA
    | CoreSig.SK_DSA _ -> SA_DSA

let sigalg_of_pkeyparams = function
    | CoreSig.PK_RSA _ -> SA_RSA
    | CoreSig.PK_DSA _ -> SA_DSA

#if ideal
// We maintain two logs:
// - a log of honest public keys (a,pk), not necessarily with strong crypto
// - a log of (a,pk,t) entries for all honestly signed texts
// CF We could also implement it on top of ideal non-agile Sigs.

type entry = alg * pkey * text 
// type entry = a:alg * pk:(;a) pk * t:text * s:(;a) sigv { Msg(a,pk,t) } 

let honest_log = ref ([]: (alg * skey * pkey) list)
let log        = ref ([]: entry list)

(* MK assoc and pk_of_log are unused and assoc doesn't make any sense.
let rec assoc hll pk =
    match hll with
      | (pk',sk')::_ when pk=pk' -> Some () // MK !!
      | _::hll                   -> assoc hll pk
      | []                       -> None


let rec pk_of_log sk hll =
    match hll with
        (pk',sk')::hll_tail when sk=sk' -> Some (pk')
      | _::hll_tail -> pk_of_log sk hll_tail
      | [] -> None
*)

let pk_of (sk:skey) =  
    let _,_,pk = (find (fun (_,sk',_) -> sk=sk') !honest_log )  
    pk

let honest a pk = exists (fun (a',_,pk') -> a=a' && pk=pk') !honest_log 
let strong a = if a=(SA_DSA ,SHA384) then true else false
#endif

(* ------------------------------------------------------------------------ *)
let sign (a: alg) (sk: skey) (t: text): sigv =
    let asig, ahash = a in
    let { skey = (kparams, khash) } = sk in

    if ahash <> khash then
        #if verify
        Error.unexpectedError("Sig.sign")
        #else
        Error.unexpectedError
            (sprintf "Sig.sign: requested sig-hash = %A, but key requires %A"
                ahash khash)
        #endif
    if asig <> sigalg_of_skeyparams kparams then
        #if verify
        Error.unexpectedError("Sig.sign")
        #else
        Error.unexpectedError
            (sprintf "Sig.sign: requested sig-algo = %A, but key requires %A"
                asig (sigalg_of_skeyparams kparams))
        #endif

    let signature =
        match khash with
        | NULL    -> CoreSig.sign None                     kparams t
        | MD5     -> CoreSig.sign (Some CoreSig.SH_MD5)    kparams t
        | SHA     -> CoreSig.sign (Some CoreSig.SH_SHA1  ) kparams t
        | SHA256  -> CoreSig.sign (Some CoreSig.SH_SHA256) kparams t
        | SHA384  -> CoreSig.sign (Some CoreSig.SH_SHA384) kparams t
        | MD5SHA1 ->
            let t = HASH.hash MD5SHA1 t in
            CoreSig.sign None kparams t
    #if ideal
    log := (a, pk_of sk, t)::!log
    #endif
    signature

(* ------------------------------------------------------------------------ *)
let verify (a : alg) (pk : pkey) (t : text) (s : sigv) =
    let asig, ahash = a in
    let { pkey = (kparams, khash) } = pk in

    if ahash <> khash then
        #if verify
        Error.unexpectedError("Sig.verify")
        #else
        Error.unexpectedError
            (sprintf "Sig.verify: requested sig-hash = %A, but key requires %A"
                ahash khash)
        #endif
    if asig <> sigalg_of_pkeyparams kparams then
        #if verify
        Error.unexpectedError("Sig.verify")
        #else
        Error.unexpectedError
            (sprintf "Sig.verify: requested sig-algo = %A, but key requires %A"
                asig (sigalg_of_pkeyparams kparams))
        #endif

    let result =
        match khash with
        | NULL    -> CoreSig.verify None                     kparams t s
        | MD5     -> CoreSig.verify (Some CoreSig.SH_MD5)    kparams t s
        | SHA     -> CoreSig.verify (Some CoreSig.SH_SHA1  ) kparams t s
        | SHA256  -> CoreSig.verify (Some CoreSig.SH_SHA256) kparams t s
        | SHA384  -> CoreSig.verify (Some CoreSig.SH_SHA384) kparams t s
        | MD5SHA1 ->
            let t = HASH.hash MD5SHA1 t in
            CoreSig.verify None kparams t s
    #if ideal //#begin-idealization
    let result = if strong a && honest a pk  
                    then result && memr !log (a,pk,t)
                    else result 
    #endif //#end-idealization
    result

(* ------------------------------------------------------------------------ *)
let gen (a:alg) : pkey * skey =
    let asig, ahash  = a in
    let (pkey, skey) =
        match asig with
        | SA_RSA -> CoreSig.gen CoreSig.CORE_SA_RSA
        | SA_DSA -> CoreSig.gen CoreSig.CORE_SA_DSA
        | _      -> Error.unexpectedError "[gen] invoked on unsupported algorithm"
    let p,s =  ({ pkey = (pkey, ahash) }, { skey = (skey, ahash) })
    #if ideal
    honest_log := (a,s,p)::!honest_log
    #endif
    (p,s)

let leak (a:alg) (s:skey) : CoreSig.sigskey = 
    let (sk, ahash) = s.skey
    sk

let create_pkey (a : alg) (p : CoreSig.sigpkey):pkey = 
    let (_,ahash)=a in
    { pkey = (p, ahash) }

let coerce (a:alg)  (p:pkey)  (csk:CoreSig.sigskey) : skey =
    let (_,ahash)=a in
    { skey = (csk, ahash) }
    //MK create_skey ahash csk

