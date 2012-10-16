﻿module CoreACiphers

open Org.BouncyCastle.Math
open Org.BouncyCastle.Crypto
open Org.BouncyCastle.Crypto.Encodings
open Org.BouncyCastle.Crypto.Engines
open Org.BouncyCastle.Crypto.Parameters

type modulus  = byte[]
type exponent = byte[]

type sk = RSASKey of CoreKeys.rsaskey
type pk = RSAPKey of CoreKeys.rsapkey

type plain = byte[]
type ctxt  = byte[]

let encrypt_pkcs1 (RSAPKey (m, e)) (plain : plain) =
    let m, e   = new BigInteger(1, m), new BigInteger(1, e) in
    let engine = new RsaEngine() in
    let engine = new Pkcs1Encoding(engine) in

    engine.Init(true, new RsaKeyParameters(false, m, e))
    engine.ProcessBlock(plain, 0, plain.Length)

let decrypt_pkcs1 (RSASKey (m, e)) (ctxt : ctxt) =
    let m, e   = new BigInteger(1, m), new BigInteger(1, e) in
    let engine = new RsaEngine() in
    let engine = new Pkcs1Encoding(engine) in

    try
        engine.Init(false, new RsaKeyParameters(true, m, e))
        Some (engine.ProcessBlock(ctxt, 0, ctxt.Length))
    with :? InvalidCipherTextException ->
        None
