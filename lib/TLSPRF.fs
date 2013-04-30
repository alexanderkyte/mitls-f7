module TLSPRF

(* Low-level (bytes -> byte) PRF implementations for TLS *)

open Bytes
open TLSConstants
open HASH
open HMAC

(* SSL3 *)

let ssl_prf_int secret label seed =
  let allData = utf8 label @| secret @| seed in
  let step1 = hash SHA allData in
  let allData = secret @| step1 in
  hash MD5 allData

let ssl_prf secret seed nb = 
  let gen_label (i:int) = new System.String(char((int 'A')+i),i+1) in
  let rec apply_prf res n = 
    if n > nb then 
      let r,_ = split res nb in r
    else
        let step1 = ssl_prf_int secret (gen_label (n/16)) seed in
        apply_prf (res @| step1) (n+16)
  in
  apply_prf empty_bytes  0

let ssl_verifyData ms ssl_sender data =
  let mm = data @| ssl_sender @| ms in
  let inner_md5  = hash MD5 (mm @| ssl_pad1_md5) in
  let outer_md5  = hash MD5 (ms @| ssl_pad2_md5 @| inner_md5) in
  let inner_sha1 = hash SHA (mm @| ssl_pad1_sha1) in
  let outer_sha1 = hash SHA (ms @| ssl_pad2_sha1 @| inner_sha1) in
  outer_md5 @| outer_sha1

let ssl_certificate_verify ms log hashAlg =
  let (pad1,pad2) =
      match hashAlg with
      | SHA -> (ssl_pad1_sha1, ssl_pad2_sha1)
      | MD5 -> (ssl_pad1_md5,  ssl_pad2_md5)
      | _ -> Error.unexpected "[ssl_certificate_verify] invoked on a wrong hash algorithm"
  let forStep1 = log @| ms @| pad1 in
  let step1 = hash hashAlg forStep1 in
  let forStep2 = ms @| pad2 @| step1 in
  hash hashAlg forStep2

(* TLS 1.0 and 1.1 *)


let rec p_hash_int alg secret seed len it aPrev acc =
  let aCur = MAC alg secret aPrev in
  let pCur = MAC alg secret (aCur @| seed) in
  if it = 1 then
    let hs = macSize alg in
    let r = len%hs in
    let (pCur,_) = split pCur r in
    acc @| pCur
  else
    p_hash_int alg secret seed len (it-1) aCur (acc @| pCur)

let p_hash alg secret seed len =
  let hs = macSize alg in
  let it = (len/hs)+1 in
  p_hash_int alg secret seed len it seed empty_bytes

let tls_prf secret label seed len =
  let l_s = length secret in
  let l_s1 = (l_s+1)/2 in
  let secret1,secret2 = split secret l_s1 in
  let newseed = (utf8 label) @| seed in
  let hmd5 = p_hash (MA_HMAC(MD5)) secret1 newseed len in
  let hsha1 = p_hash (MA_HMAC(SHA)) secret2 newseed len in
  xor hmd5 hsha1 len

let tls_verifyData ms tls_label data =
  let md5hash  = hash MD5 data in
  let sha1hash = hash SHA data in
  tls_prf ms tls_label (md5hash @| sha1hash) 12

(* TLS 1.2 *)

(* internal, shared between the two functions below *)

let tls12prf cs ms label data len =
  let prfMacAlg = prfMacAlg_of_ciphersuite cs in
  p_hash prfMacAlg ms (utf8 label @| data) len

let tls12VerifyData cs ms tls_label data =
  let verifyDataHashAlg = verifyDataHashAlg_of_ciphersuite cs in
  let verifyDataLen = verifyDataLen_of_ciphersuite cs in
  let hashed = hash verifyDataHashAlg data in
  tls12prf cs ms tls_label hashed verifyDataLen

(* Internal generic (SSL/TLS) implementation of PRF *)

let prf pv cs secret label data len =
  match pv with 
  | SSL_3p0           -> ssl_prf     secret       data len
  | TLS_1p0 | TLS_1p1 -> tls_prf     secret label data len
  | TLS_1p2           -> tls12prf cs secret label data len
