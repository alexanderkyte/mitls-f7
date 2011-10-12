﻿module TLSCrypto

open Data
open Error_handling
open StdCrypto
open Algorithms
open Formats
open HS_ciphersuites

(* MAC *)
let MAC pv alg key data =
    match pv with
    | ProtocolVersionType.SSL_3p0 -> sslKeyedHash alg key data
    | x when x >= ProtocolVersionType.TLS_1p0 -> hmac alg key data
    | _ -> Error(MAC, Unsupported)


(* Low-level prf functions *)
type masterSecret = bytes

(* SSL *)
let ssl_verifyData ms role data =
    let ssl_sender_client = [|0x43uy; 0x4Cuy; 0x4Euy; 0x54uy|] in
    let ssl_sender_server = [|0x53uy; 0x52uy; 0x56uy; 0x52uy|] in
    let ssl_sender = 
        match role with
        | ClientRole -> ssl_sender_client 
        | ServerRole -> ssl_sender_server
    let mm = append data (append ssl_sender ms) in
    match hash MD5 (append mm ssl_pad1_md5) with
    | Error (x,y) -> Error(x,y)
    | Correct (inner_md5) ->
        match hash MD5 (append ms (append ssl_pad2_md5 (inner_md5))) with
        | Error (x,y) -> Error(x,y)
        | Correct (outer_md5) ->
            match hash SHA (append mm ssl_pad1_sha1) with
            | Error (x,y) -> Error(x,y)
            | Correct(inner_sha1) ->
                match hash SHA (append ms (append ssl_pad2_sha1 (inner_sha1))) with
                | Error (x,y) -> Error(x,y)
                | Correct (outer_sha1) ->
                    correct (append outer_md5 outer_sha1)

(* TLS 1.0; 1.1 *)
let xor s1 s2 nb =
  if Array.length s1 < nb || Array.length s2 < nb then
    unexpectedError "[xor] arrays too short"
  else
    let res = Array.zeroCreate nb in  
    for i=0 to nb-1 do
      res.[i] <- byte (int s1.[i] ^^^ int s2.[i])
    done;
    res

let rec p_hash_int alg secret seed len it aPrev acc =
    match hmac alg secret aPrev with
    | Error (x,y) -> Error(x,y)
    | Correct(aCur) ->
        match hmac alg secret (append aCur seed) with
        | Error(x,y) -> Error(x,y)
        | Correct(pCur) ->
            if it = 1 then
                let hs = hashSize alg in
                let r = len%hs in
                let (pCur,_) = split pCur r in
                correct (append acc pCur)
            else
                p_hash_int alg secret seed len (it-1) aCur (append acc pCur)

let p_hash alg secret seed len =
    let hs = hashSize alg in
    let it = (len/hs)+1 in
    p_hash_int alg secret seed len it seed Bytearray.empty_bstr

let tls_prf secret label seed len =
  let l_s = Array.length secret in
  let l_s1 = (l_s+1)/2 in
  let secret1 = Array.sub secret 0 l_s1 in
  let secret2 = Array.sub secret (l_s-l_s1) l_s1 in
  let newseed = append (utf8 label) seed in
  match p_hash MD5 secret1 newseed len with
  | Error (x,y) -> Error(x,y)
  | Correct (hmd5) ->
    match p_hash SHA secret2 newseed len with
    | Error(x,y) -> Error(x,y)
    | Correct (hsha1) ->
        correct (xor hmd5 hsha1 len)

let tls_verifyData ms role data =
    let tls_label = 
        match role with
        | ClientRole -> "client finished"
        | ServerRole -> "server finished"
    match hash MD5 data with
    | Error (x,y) -> Error(x,y)
    | Correct (md5hash) ->
        match hash SHA data with
        | Error (x,y) -> Error(x,y)
        | Correct (sha1hash) ->
            match tls_prf ms tls_label (append md5hash sha1hash) 12 with
            | Error (x,y) -> Error(x,y)
            | Correct (result) -> correct (result)

(* TLS 1.2 *)
let tls12prf cs secret label seed len =
    let prfHashAlg = prfHashAlg_of_ciphersuite cs in
    let newseed = append (utf8 label) seed in
    p_hash prfHashAlg secret newseed len

let tls12VerifyData cs ms role data =
    let tls_label = 
        match role with
        | ClientRole -> "client finished"
        | ServerRole -> "server finished"
    let verifyDataHashAlg = verifyDataHashAlg_of_ciphersuite cs in
    match hash verifyDataHashAlg data with
    | Error (x,y) -> Error(HSError(AD_decrypt_error),HSSendAlert)
    | Correct(hashResult) ->
        let verifyDataLen = verifyDataLen_of_ciphersuite cs in
        match tls12prf cs ms tls_label hashResult verifyDataLen with
        | Error (x,y) -> Error(x,y)
        | Correct(result) -> correct (result)

(* High-level prf functions -- implement interface *)

let prfVerifyData pv cs ms role data =
  match pv with 
  | ProtocolVersionType.SSL_3p0 -> ssl_verifyData ms role data
  | x when x = ProtocolVersionType.TLS_1p0 || x = ProtocolVersionType.TLS_1p1 -> 
    tls_verifyData ms role data
  | ProtocolVersionType.TLS_1p2 ->
    tls12VerifyData cs ms role data
  | _ -> unexpectedError "[prfVerifyData] invoked on unsupported protocol version"