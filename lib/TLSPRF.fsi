﻿module TLSPRF

open Bytes
open TLSConstants
open TLSInfo

val verifyData: vdAlg -> bytes -> Role -> bytes -> bytes 
val extract: kefAlg -> bytes -> bytes -> int -> bytes
val kdf: kdfAlg -> bytes -> bytes -> int -> bytes

(* SSL-specific certificate verify *)

val ssl_verifyCertificate: hashAlg -> bytes -> bytes -> bytes