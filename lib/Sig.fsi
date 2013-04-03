﻿module Sig

open Bytes
open TLSConstants

(* ------------------------------------------------------------------------ *)
type alg   = sigHashAlg //MK: now defined in TLSConstants.fs7: sigAlg * hashAlg

type text = bytes
type sigv = bytes 

(* ------------------------------------------------------------------------ *)
type skey
type pkey

//val create_skey: hashAlg -> CoreSig.sigskey -> skey
val create_pkey: alg -> CoreSig.sigpkey -> pkey

//val repr_of_skey: skey -> CoreSig.sigskey * hashAlg
//val repr_of_pkey: pkey -> CoreSig.sigpkey * hashAlg

val sigalg_of_skeyparams : CoreSig.sigskey -> sigAlg
val sigalg_of_pkeyparams : CoreSig.sigpkey -> sigAlg

(* ------------------------------------------------------------------------ *)
val gen    : alg -> pkey * skey
val sign   : alg -> skey -> text -> sigv
val verify : alg -> pkey -> text -> sigv -> bool
val coerce :  alg -> pkey -> CoreSig.sigskey -> skey