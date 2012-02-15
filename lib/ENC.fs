﻿module ENC

open Bytes
open Error
open Algorithms
open CipherSuites
open TLSInfo
open Formats

type cipher = bytes

(* Raw symmetric enc/dec functions -- 
   Although in principle these functions could throw exceptions,
   we claim they can never do that:
   ArgumentException: we always pass a readable/writable and valid stream
   NotSupportedException: we always give a writable (readable) stream to write (read)
   ArgumentOutOfRangeException: data.Length is always greater than zero
   ArgumentException: 0+data.Length is never longer than data.Length
   CryptographicException: The key is never corrupt
*)

let symEncrypt enc data =
    let mems = new System.IO.MemoryStream() in
    let crs = new System.Security.Cryptography.CryptoStream(mems,enc,System.Security.Cryptography.CryptoStreamMode.Write) in
    crs.Write(data,0,data.Length) 
    crs.FlushFinalBlock() 
    let cipher = mems.ToArray() in
    mems.Close();
    crs.Close();
    cipher

let symDecrypt dec (data:bytes) =
    let mems = new System.IO.MemoryStream(data) in
    let crs = new System.Security.Cryptography.CryptoStream(mems,dec,System.Security.Cryptography.CryptoStreamMode.Read) in
    let plain = Array.zeroCreate(data.Length) in  
    let _ =  crs.Read(plain,0,plain.Length) in
    plain

let aesEncrypt ki key iv data =
    let aes = new System.Security.Cryptography.AesManaged() in
    let k = ENCKey.LEAK ki key
    aes.KeySize <- 8 * length k
    aes.Padding <- System.Security.Cryptography.PaddingMode.None
    let enc = aes.CreateEncryptor(k,iv) in
    symEncrypt enc data

let aesDecrypt ki key iv (data:bytes) =
    let aes = new System.Security.Cryptography.AesManaged() in
    let k = ENCKey.LEAK ki key
    aes.KeySize <- 8 * length k
    aes.Padding <- System.Security.Cryptography.PaddingMode.None;
    let dec = aes.CreateDecryptor(k,iv) in
    symDecrypt dec data
       
let tdesEncrypt ki key iv data =
    let tdes = new System.Security.Cryptography.TripleDESCryptoServiceProvider() in
    tdes.Padding <- System.Security.Cryptography.PaddingMode.None
    let k = ENCKey.LEAK ki key
    let enc = tdes.CreateEncryptor(k,iv) in
    symEncrypt enc data

let tdesDecrypt ki key iv (data:bytes) =
    let tdes = new System.Security.Cryptography.TripleDESCryptoServiceProvider() in
    tdes.Padding <- System.Security.Cryptography.PaddingMode.None;
    let k = ENCKey.LEAK ki key
    let dec = tdes.CreateDecryptor(k,iv) in
    symDecrypt dec data

(* Early TLS chains IVs but this is not secure against adaptive CPA *)
let lastblock cipher ivl =
    let (_,b) = split cipher (length cipher - ivl) in b

(* Parametric ENC/DEC functions *)
let ENC ki key iv3 (tlen:DataStream.range) data =
    (* Should never be invoked on a stream (right now) encryption algorithm *)
    let alg = encAlg_of_ciphersuite ki.sinfo.cipher_suite in
    let ivl = ivSize alg in
    let iv =
        match iv3 with
        | ENCKey.SomeIV(b) -> b
        | ENCKey.NoIV _    -> mkRandom ivl in
    let d = AEPlain.repr ki tlen data in
    let min,max = tlen in
    let cipher =
        match alg with
        | TDES_EDE_CBC -> tdesEncrypt ki key iv d
        | AES_128_CBC  -> aesEncrypt  ki key iv d
        | AES_256_CBC  -> aesEncrypt  ki key iv d
        | RC4_128      -> unexpectedError "[ENC] invoked on stream cipher"
    match iv3 with
    | ENCKey.SomeIV(_) ->
        if length cipher <> max || max > FragCommon.max_TLSCipher_fragment_length then
            // unexpected, because it is enforced statically by the
            // CompatibleLength predicate
            unexpectedError "[ENC] Length of encrypted data do not match expected length"
        else
            (ENCKey.SomeIV(lastblock cipher ivl), cipher)
    | ENCKey.NoIV(b) ->
        let res = iv @| cipher in
        if length res <> max || max > FragCommon.max_TLSCipher_fragment_length then
            // unexpected, because it is enforced statically by the
            // CompatibleLength predicate
            unexpectedError "[ENC] Length of encrypted data do not match expected length"
        else
            (ENCKey.NoIV(b), res)

let DEC ki key iv3 cipher =
    (* Should never be invoked on a stream (right now) encryption algorithm *)
    let alg = encAlg_of_ciphersuite ki.sinfo.cipher_suite in
    let ivl = ivSize alg 
    let (iv,encrypted) =
        match iv3 with
        | ENCKey.SomeIV (iv) -> (iv,cipher)
        | ENCKey.NoIV (b)    -> split cipher ivl
    let data =
        match alg with
        | TDES_EDE_CBC -> tdesDecrypt ki key iv encrypted
        | AES_128_CBC  -> aesDecrypt  ki key iv encrypted
        | AES_256_CBC  -> aesDecrypt  ki key iv encrypted
        | RC4_128      -> unexpectedError "[DEC] invoked on stream cipher"
    let d = AEPlain.plain ki (length cipher,length cipher) data in
    match iv3 with
    | ENCKey.SomeIV(_) -> (ENCKey.SomeIV(lastblock cipher ivl), d)
    | ENCKey.NoIV(b)   -> (ENCKey.NoIV(b), d)

(* the SPRP game in F#, without indexing so far.
   the adversary gets 
   enc: block -> block
   dec: block -> block 

// two copies of assoc 
let rec findp pcs c = 
  match pcs with 
  | (p,c')::pcs -> if c = c' then Some(p) else findp pcs c
  | [] -> None
let rec findc pcs p = 
  match pcs with 
  | (p',c)::pcs -> if p = p' then Some(c) else findc pcs p
  | [] -> None
   
let k = mkRandom blocksize
let qe = ref 0
let qd = ref 0
#if aes
let F = AES k
let G = AESminus k 
#else
let log = ref ([] : (block * block) list)
let F p = 
  match findc !pcs p with 
  | Some(c) -> c // non-parametric; 
                 // after CBC-collision avoidance,
                 // we will always use the "None" case
  | None    -> let c = mkfreshc !log blocksize 
               log := (p,c)::!log
               c
let G c = 
  match findp !log c with 
  | Some(p) -> p 
  | None    -> let p = mkfreshp !log blocksize 
               log := (p,c)::!log
               p
#endif
let enc p = incr qe; F p
let dec c = incr qd; G c
*)
