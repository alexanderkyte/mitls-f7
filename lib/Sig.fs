﻿module Sig 

// see the .fs7 for comments and discussion

type sigAlg = 
  | RSA //[8.1.1] RSA digital signatures are performed using PKCS #1 block type 1. 
  | DSA 
  | ECDSA

type hashAlg = // annoyingly not the same as Algorithms.hashAlg
  | MD5    // 1
  | SHA1   // 2
  | SHA224 // 3
  | SHA256 // 4
  | SHA384 // 5
  | SHA512 // 6
  
type alg = //
  sigAlg * Algorithms.hashAlg // hashAlgo does *not* include some values from the spec.

type text = bytes
type sigv = bytes 

let defaultAlg cs =
  let a = 
    match cs with 
    | RSA | DH_RSA | DHE_RSA | RSA_PSK (* | ECDH_RSA | ECDHE_RSA *) -> RSA
    | DH_DSS | DHE_DSS                                              -> DSA
  (*| ECDH_ECDSA | ECDHE_ECDSA                                      -> ECDSA *)
  (a, SHA1)

type skey 
type vkey = bytes 

let Gen     (a:alg): vkey * skey = failwith "todo"
let Sign    (a:alg) (sk:skey) (t:text) : sigv = failwith "todo"
let Verify: (a:alg) (vk:vkey) (t:text) (v:sigv) : bool = failwith "todo"

let PkBytes (a:alg) (vk:vkey) : bytes = failwith "todo"
let SkBytes (a:alg) (sk:skey) : bytes = failwith "todo"


