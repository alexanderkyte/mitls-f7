﻿module Sig 

open Bytes
open TLSConstants

(* ------------------------------------------------------------------------ *)
type alg   = sigAlg * hashAlg

type text = bytes
type sigv = bytes 

(* ------------------------------------------------------------------------ *)
type skey = { skey : CoreSig.sigskey * hashAlg }
type pkey = { pkey : CoreSig.sigpkey * hashAlg }

let create_skey (h : hashAlg) (p : CoreSig.sigskey) = { skey = (p, h) }
let create_pkey (h : hashAlg) (p : CoreSig.sigpkey) = { pkey = (p, h) }

let repr_of_skey { skey = skey } = skey
let repr_of_pkey { pkey = pkey } = pkey

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

// MK this assoc is unused and doesn't make any sense.
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
        Error.unexpectedError
            (sprintf "Sig.sign: requested sig-hash = %A, but key requires %A"
                ahash khash)
    if asig <> sigalg_of_skeyparams kparams then
        Error.unexpectedError
            (sprintf "Sig.sign: requested sig-algo = %A, but key requires %A"
                asig (sigalg_of_skeyparams kparams))

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
        Error.unexpectedError
            (sprintf "Sig.verify: requested sig-hash = %A, but key requires %A"
                ahash khash)
    if asig <> sigalg_of_pkeyparams kparams then
        Error.unexpectedError
            (sprintf "Sig.verify: requested sig-algo = %A, but key requires %A"
                asig (sigalg_of_pkeyparams kparams))

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
    #if ideal
    let result = if strong a && honest a pk  
                    then result && memr !log (a,pk,t)
                    else result 
    #endif
    result

(* ------------------------------------------------------------------------ *)
let gen (a:alg) : pkey * skey =
    let asig, ahash  = a in
    let (pkey, skey) =
        match asig with
        | SA_RSA -> CoreSig.gen CoreSig.SA_RSA
        | SA_DSA -> CoreSig.gen CoreSig.SA_DSA
        | _      -> failwith "unsupported / TODO"
    let p,s =  ({ pkey = (pkey, ahash) }, { skey = (skey, ahash) })
    #if ideal
    honest_log := (a,s,p)::!honest_log
    #endif
    (p,s)