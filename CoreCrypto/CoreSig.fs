﻿module CoreSig
open Bytes

(* ------------------------------------------------------------------------ *)
open System

open Org.BouncyCastle.Math
open Org.BouncyCastle.Crypto
open Org.BouncyCastle.Crypto.Digests
open Org.BouncyCastle.Crypto.Generators
open Org.BouncyCastle.Crypto.Signers
open Org.BouncyCastle.Crypto.Parameters
open Org.BouncyCastle.Math.EC
open Org.BouncyCastle.Security

(* ------------------------------------------------------------------------ *)
type sighash =
| SH_MD5
| SH_SHA1
| SH_SHA256
| SH_SHA384

type sigalg =
| CORE_SA_RSA
| CORE_SA_DSA
| CORE_SA_ECDSA

(* ------------------------------------------------------------------------ *)
type sigskey =
| SK_RSA of CoreKeys.rsaskey
| SK_DSA of CoreKeys.dsaskey
| SK_ECDH of CoreKeys.ecdhskey

type sigpkey =
| PK_RSA of CoreKeys.rsapkey
| PK_DSA of CoreKeys.dsapkey
| PK_ECDH of CoreKeys.ecdhpkey

type text = bytes
type sigv = bytes

(* ------------------------------------------------------------------------ *)
let sigalg_of_skey = function
    | SK_RSA _ -> CORE_SA_RSA
    | SK_DSA _ -> CORE_SA_DSA
    | SK_ECDH _ -> CORE_SA_ECDSA

let sigalg_of_pkey = function
    | PK_RSA _ -> CORE_SA_RSA
    | PK_DSA _ -> CORE_SA_DSA
    | PK_ECDH _ -> CORE_SA_ECDSA

(* ------------------------------------------------------------------------ *)
let bytes_to_bigint (b : bytes) = new BigInteger(1, cbytes b)
let bytes_of_bigint (b : BigInteger) = abytes (b.ToByteArrayUnsigned())

(* ------------------------------------------------------------------------ *)
let new_hash_engine (h : sighash option) : IDigest =
    let new_hash_engine (h : sighash) : IDigest =
        match h with
        | SH_MD5    -> (new MD5Digest   () :> IDigest)
        | SH_SHA1   -> (new Sha1Digest  () :> IDigest)
        | SH_SHA256 -> (new Sha256Digest() :> IDigest)
        | SH_SHA384 -> (new Sha384Digest() :> IDigest)
    in
        match h with
        | None   -> new NullDigest () :> IDigest
        | Some h -> new_hash_engine h

(* ------------------------------------------------------------------------ *)
let new_rsa_signer (h : sighash option) =
    new RsaDigestSigner(new_hash_engine h)

(* ------------------------------------------------------------------------ *)
let RSA_sign ((m, e) : CoreKeys.rsaskey) (h : sighash option) (t : text) : sigv =
    let signer = new_rsa_signer h in

    signer.Init(true, new RsaKeyParameters(true, bytes_to_bigint m, bytes_to_bigint e))
    signer.BlockUpdate(cbytes t, 0, length t)
    abytes (signer.GenerateSignature())

let RSA_verify ((m, e) : CoreKeys.rsapkey) (h : sighash option) (t : text) (s : sigv) =
    let signer = new_rsa_signer h in

    signer.Init(false, new RsaKeyParameters(false, bytes_to_bigint m, bytes_to_bigint e))
    signer.BlockUpdate(cbytes t, 0, length t)
    signer.VerifySignature(cbytes s)

let RSA_gen () =
    let generator = new RsaKeyPairGenerator() in
    generator.Init(new KeyGenerationParameters(new SecureRandom(), 2048))
    let keys = generator.GenerateKeyPair() in
    let vkey = (keys.Public  :?> RsaKeyParameters) in
    let skey = (keys.Private :?> RsaKeyParameters) in

    (PK_RSA (bytes_of_bigint vkey.Modulus, bytes_of_bigint vkey.Exponent),
     SK_RSA (bytes_of_bigint skey.Modulus, bytes_of_bigint skey.Exponent))

(* ------------------------------------------------------------------------ *)
let bytes_of_dsaparams p q g : CoreKeys.dsaparams =
    { p = bytes_of_bigint p;
      q = bytes_of_bigint q;
      g = bytes_of_bigint g; }

let DSA_sign ((x, dsap) : CoreKeys.dsaskey) (h : sighash option) (t : text) : sigv =
    let signer    = new DsaDigestSigner(new DsaSigner(), new_hash_engine h) in
    let dsaparams = new DsaParameters(bytes_to_bigint dsap.p,
                                      bytes_to_bigint dsap.q,
                                      bytes_to_bigint dsap.g)

    signer.Init(true, new DsaPrivateKeyParameters(bytes_to_bigint x, dsaparams))
    signer.BlockUpdate(cbytes t, 0, length t)
    abytes (signer.GenerateSignature())

let DSA_verify ((y, dsap) : CoreKeys.dsapkey) (h : sighash option) (t : text) (s : sigv) =
    let signer    = new DsaDigestSigner(new DsaSigner(), new_hash_engine h) in
    let dsaparams = new DsaParameters(bytes_to_bigint dsap.p,
                                      bytes_to_bigint dsap.q,
                                      bytes_to_bigint dsap.g)

    signer.Init(false, new DsaPublicKeyParameters(bytes_to_bigint y, dsaparams))
    signer.BlockUpdate(cbytes t, 0, length t)
    signer.VerifySignature(cbytes s)

let DSA_gen () =
    let paramsgen = new DsaParametersGenerator() in
    paramsgen.Init(2048, 80, new SecureRandom())
    let dsaparams = paramsgen.GenerateParameters() in
    let generator = new DsaKeyPairGenerator() in
    generator.Init(new DsaKeyGenerationParameters(new SecureRandom(), dsaparams))
    let keys = generator.GenerateKeyPair() in
    let vkey = (keys.Public  :?> DsaPublicKeyParameters) in
    let skey = (keys.Private :?> DsaPrivateKeyParameters) in

    (PK_DSA (bytes_of_bigint vkey.Y, bytes_of_dsaparams dsaparams.P dsaparams.Q dsaparams.G),
     SK_DSA (bytes_of_bigint skey.X, bytes_of_dsaparams dsaparams.P dsaparams.Q dsaparams.G))

(* ------------------------------------------------------------------------ *)
let ECDSA_gen () =
    let curve, ecdom, basep = CoreECDH.getcurve ""
    let ecparam = new ECKeyGenerationParameters(ecdom, new SecureRandom())
    let gen = new ECKeyPairGenerator()
    gen.Init(ecparam)
    let keys = gen.GenerateKeyPair()
    let pk = (keys.Public :?> ECPublicKeyParameters)
    let sk = (keys.Private :?> ECPrivateKeyParameters)
    let x = pk.Q.X.ToBigInteger()
    let y = pk.Q.Y.ToBigInteger()
    let parms = { CoreKeys.curve_name = "secp256r1"; }
    let pub = { CoreKeys.ecx = bytes_of_bigint x; CoreKeys.ecy = bytes_of_bigint y; }
    let priv = bytes_of_bigint sk.D
    (PK_ECDH (pub, parms), SK_ECDH (priv, parms))

let getsigalg = function
    | None -> "NONEwithECDSA"
    | Some h ->
        match h with
        | SH_SHA1 -> "SHA1withECDSA"
        | SH_SHA256 -> "SHA256withECDSA"
        | SH_SHA384 -> "SHA384withECDSA"
        | _ -> failwith "Hash algorithm not supported with ECDSA"

let ECDSA_sign (sk : CoreKeys.ecdhskey) (ahash : sighash option) (t : text) =
    let (D, parms) = sk
    let curve, ecdom, basep = CoreECDH.getcurve parms.curve_name
    let signer = SignerUtilities.GetSigner(getsigalg ahash)
    signer.Init(true, new ECPrivateKeyParameters(bytes_to_bigint D, ecdom))
    signer.BlockUpdate(cbytes t, 0, length t)
    abytes (signer.GenerateSignature())

let ECDSA_verify ((P, parms) : CoreKeys.ecdhpkey) (h : sighash option) (t : text) (s : sigv) =
    let curve, ecdom, basep = CoreECDH.getcurve parms.curve_name
    let signer = SignerUtilities.GetSigner(getsigalg h)
    let ECPx = new FpFieldElement(curve.Q, bytes_to_bigint P.ecx)
    let ECPy = new FpFieldElement(curve.Q, bytes_to_bigint P.ecy)
    let ECP = new FpPoint(curve, ECPx, ECPy)
    signer.Init(false, new ECPublicKeyParameters(ECP, ecdom))
    signer.BlockUpdate(cbytes t, 0, length t)
    signer.VerifySignature(cbytes s)

(* ------------------------------------------------------------------------ *)
let sign (ahash : sighash option) (sk : sigskey) (t : text) : sigv =
    match sk with
    | SK_RSA sk -> RSA_sign sk ahash t
    | SK_DSA sk -> DSA_sign sk ahash t
    | SK_ECDH sk -> ECDSA_sign sk ahash t

(* ------------------------------------------------------------------------ *)
let verify (ahash : sighash option) (pk : sigpkey) (t : text) (s : sigv) =
    match pk with
    | PK_RSA pk -> RSA_verify pk ahash t s
    | PK_DSA pk -> DSA_verify pk ahash t s
    | PK_ECDH pk -> ECDSA_verify pk ahash t s

(* ------------------------------------------------------------------------ *)
let gen (a : sigalg) : sigpkey * sigskey =
    match a with
    | CORE_SA_RSA   -> RSA_gen ()
    | CORE_SA_DSA   -> DSA_gen ()
    | CORE_SA_ECDSA -> ECDSA_gen ()
