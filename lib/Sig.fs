﻿module Sig 

// see the .fs7 for comments and discussion
open Bytes
open Algorithms

open Org.BouncyCastle.Math
open Org.BouncyCastle.Crypto
open Org.BouncyCastle.Crypto.Digests
open Org.BouncyCastle.Crypto.Generators
open Org.BouncyCastle.Crypto.Signers
open Org.BouncyCastle.Crypto.Parameters
open Org.BouncyCastle.Security

(* ------------------------------------------------------------------------ *)
type sigAlg = 
  | SA_RSA //[8.1.1] RSA digital signatures are performed using PKCS #1 block type 1. 
  | SA_DSA 
  | SA_ECDSA

type hashAlg = // annoyingly not the same as Algorithms.hashAlg
  | MD5    // 1
  | SHA1   // 2
  | SHA224 // 3
  | SHA256 // 4
  | SHA384 // 5
  | SHA512 // 6
  
type alg = sigAlg * Algorithms.hashAlg // hashAlg does *not* include some values from the spec.

type text = bytes
type sigv = bytes 

(* ------------------------------------------------------------------------ *)
type dsaparams = { p : bytes; q : bytes; g : bytes; }

type skeyparams =
| SK_RSA of bytes * bytes (* modulus x exponent *)
| SK_DSA of bytes * dsaparams

type vkeyparams =
| VK_RSA of bytes * bytes
| VK_DSA of bytes * dsaparams

type skey = SKey of skeyparams * hashAlg
type vkey = VKey of vkeyparams * hashAlg

(* ------------------------------------------------------------------------ *)
let defaultAlg cs =
  let a = 
    match cs with 
    | RSA | DH_RSA | DHE_RSA (* | RSA_PSK | ECDH_RSA | ECDHE_RSA *) -> SA_RSA
    | DH_DSS | DHE_DSS                                              -> SA_DSA
  (*| ECDH_ECDSA | ECDHE_ECDSA                                      -> SA_ECDSA *)
  (a, SHA1)

(* ------------------------------------------------------------------------ *)
let sighash_of_hash = function
| Algorithms.MD5    -> MD5
| Algorithms.SHA    -> SHA1
| Algorithms.SHA256 -> SHA256
| Algorithms.SHA384 -> SHA384

let sigalg_of_skeyparams = function
| SK_RSA _ -> SA_RSA
| SK_DSA _ -> SA_DSA

let sigalg_of_vkeyparams = function
| VK_RSA _ -> SA_RSA
| VK_DSA _ -> SA_DSA

(* ------------------------------------------------------------------------ *)
let bytes_to_bigint (b : bytes) = new BigInteger(b)
let bytes_of_bigint (b : BigInteger) = b.ToByteArray()

(* ------------------------------------------------------------------------ *)
let new_hash_engine (h : hashAlg) : IDigest =
    match h with
    | MD5    -> (new MD5Digest   () :> IDigest)
    | SHA1   -> (new Sha1Digest  () :> IDigest)
    | SHA224 -> (new Sha224Digest() :> IDigest)
    | SHA256 -> (new Sha256Digest() :> IDigest)
    | SHA384 -> (new Sha384Digest() :> IDigest)
    | SHA512 -> (new Sha512Digest() :> IDigest)

(* ------------------------------------------------------------------------ *)
let RSA_sign (m, e) (h : hashAlg) (t : text) : sigv =
    let signer = new RsaDigestSigner(new_hash_engine h) in

    signer.Init(true, new RsaKeyParameters(true, bytes_to_bigint m, bytes_to_bigint e))
    signer.BlockUpdate(t, 0, t.Length)
    signer.GenerateSignature()

let RSA_verify ((m, e) : bytes * bytes) (h : hashAlg) (t : text) (s : sigv) =
    let signer = new RsaDigestSigner(new_hash_engine h) in

    signer.Init(false, new RsaKeyParameters(false, bytes_to_bigint m, bytes_to_bigint e))
    signer.BlockUpdate(t, 0, t.Length)
    signer.VerifySignature(s)

let RSA_gen (h : hashAlg) =
    let generator = new RsaKeyPairGenerator() in
    generator.Init(new KeyGenerationParameters(new SecureRandom(), 2048))
    let keys = generator.GenerateKeyPair() in
    let vkey = (keys.Public  :?> RsaKeyParameters) in
    let skey = (keys.Private :?> RsaKeyParameters) in

    (VKey (VK_RSA (bytes_of_bigint vkey.Modulus, bytes_of_bigint vkey.Exponent), h),
     SKey (SK_RSA (bytes_of_bigint skey.Modulus, bytes_of_bigint skey.Exponent), h))

(* ------------------------------------------------------------------------ *)
let bytes_of_dsaparams p q g =
    { p = bytes_of_bigint p;
      q = bytes_of_bigint q;
      g = bytes_of_bigint g; }

let DSA_sign (x, dsap) (h : hashAlg) (t : text) : sigv =
    let signer    = new DsaDigestSigner(new DsaSigner(), new_hash_engine h) in
    let dsaparams = new DsaParameters(bytes_to_bigint dsap.p,
                                      bytes_to_bigint dsap.q,
                                      bytes_to_bigint dsap.g)

    signer.Init(true, new DsaPrivateKeyParameters(bytes_to_bigint x, dsaparams))
    signer.BlockUpdate(t, 0, t.Length)
    signer.GenerateSignature()

let DSA_verify (y, dsap) (h : hashAlg) (t : text) (s : sigv) =
    let signer    = new DsaDigestSigner(new DsaSigner(), new_hash_engine h) in
    let dsaparams = new DsaParameters(bytes_to_bigint dsap.p,
                                      bytes_to_bigint dsap.q,
                                      bytes_to_bigint dsap.g)

    signer.Init(false, new DsaPublicKeyParameters(bytes_to_bigint y, dsaparams))
    signer.BlockUpdate(t, 0, t.Length)
    signer.VerifySignature(s)

let DSA_gen (h : hashAlg) =
    let paramsgen = new DsaParametersGenerator() in
    paramsgen.Init(2048, 80, new SecureRandom())
    let dsaparams = paramsgen.GenerateParameters() in
    let generator = new DsaKeyPairGenerator() in
    generator.Init(new DsaKeyGenerationParameters(new SecureRandom(), dsaparams))
    let keys = generator.GenerateKeyPair() in
    let vkey = (keys.Public  :?> DsaPublicKeyParameters) in
    let skey = (keys.Private :?> DsaPrivateKeyParameters) in

    (VKey (VK_DSA (bytes_of_bigint vkey.Y, bytes_of_dsaparams dsaparams.P dsaparams.Q dsaparams.G), h),
     SKey (SK_DSA (bytes_of_bigint skey.X, bytes_of_dsaparams dsaparams.P dsaparams.Q dsaparams.G), h))

(* ------------------------------------------------------------------------ *)
let sign (a : alg) (sk : skey) (t : text) : sigv =
    let asig, ahash = a in
    let (SKey (kparams, khash)) = sk in

    if sighash_of_hash ahash <> khash then
        failwith
            (sprintf "Sig.sign: requested sig-hash = %A, but key requires %A"
                ahash khash)
    if asig <> sigalg_of_skeyparams kparams then
        failwith
            (sprintf "Sig.sign: requested sig-algo = %A, but key requires %A"
                asig (sigalg_of_skeyparams kparams))

    match kparams with
    | SK_RSA (m, e) -> RSA_sign (m, e) khash t
    | SK_DSA (x, p) -> DSA_sign (x, p) khash t

(* ------------------------------------------------------------------------ *)
let verify (a : alg) (vk : vkey) (t : text) (s : sigv) =
    let asig, ahash = a in
    let (VKey (kparams, khash)) = vk in

    if sighash_of_hash ahash <> khash then
        failwith
            (sprintf "Sig.verify: requested sig-hash = %A, but key requires %A"
                ahash khash)
    if asig <> sigalg_of_vkeyparams kparams then
        failwith
            (sprintf "Sig.verify: requested sig-algo = %A, but key requires %A"
                asig (sigalg_of_vkeyparams kparams))

    match kparams with
    | VK_RSA (m, e) -> RSA_verify (m, e) khash t s
    | VK_DSA (y, p) -> DSA_verify (y, p) khash t s

(* ------------------------------------------------------------------------ *)
let gen (a:alg) : vkey * skey =
    let asig, ahash = a in

    match asig with
    | SA_RSA   -> RSA_gen (sighash_of_hash ahash)
    | SA_DSA   -> DSA_gen (sighash_of_hash ahash)
    | SA_ECDSA -> failwith "todo"

(* ------------------------------------------------------------------------ *)
let PkBytes (a:alg) (vk : vkey) : bytes = failwith "todo"
let SkBytes (a:alg) (sk : skey) : bytes = failwith "todo"
