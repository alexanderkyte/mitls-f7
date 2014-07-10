open Bytes

type modulus  = bytes
type exponent = bytes

type sk = RSASKey of CoreKeys.rsaskey
type pk = RSAPKey of CoreKeys.rsapkey

type plain = bytes
type ctxt  = bytes

let encrypt_pkcs1 (RSAPKey (m, e)) (plain : plain) =
    let m, e   = new BigInteger(1, cbytes m), 
                 new BigInteger(1, cbytes e) in
    let engine = new RsaEngine() in
    let engine = new Pkcs1Encoding(engine) in

    engine.Init(true, new RsaKeyParameters(false, m, e))
    abytes (engine.ProcessBlock(cbytes plain, 0, length plain))

let decrypt_pkcs1 (RSASKey (m, e)) (ctxt : ctxt) =
    let m, e   = new BigInteger(1, cbytes m), 
                 new BigInteger(1, cbytes e) in
    let engine = new RsaEngine() in
    let engine = new Pkcs1Encoding(engine) in

    try
        engine.Init(false, new RsaKeyParameters(true, m, e))
        Some (abytes (engine.ProcessBlock(cbytes ctxt, 0, length ctxt)))
    with :? InvalidCipherTextException ->
        None
