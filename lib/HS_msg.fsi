﻿module HS_msg

open Bytes
open Formats
//open Record
open CipherSuites
open TLSInfo
open Principal

type HandShakeType =
    | HT_hello_request
    | HT_client_hello
    | HT_server_hello
    | HT_certificate
    | HT_server_key_exchange
    | HT_certificate_request
    | HT_server_hello_done
    | HT_certificate_verify
    | HT_client_key_exchange
    | HT_finished
    | HT_unknown of int

val htbytes: HandShakeType -> bytes
val parseHT: bytes -> HandShakeType

type Extension =
    | HExt_renegotiation_info
    | HExt_unknown of bytes

val bytes_of_HExt: Extension -> bytes
val hExt_of_bytes: bytes -> Extension

(* Message bodies *)

(* Hello Request *)
type helloRequest = bytes (* empty bitstring *)

(* Client Hello *)
type Random = {time : int; rnd : bytes}

type clientHello = {
    client_version: ProtocolVersion;
    ch_random: Random;
    ch_session_id: sessionID;
    cipher_suites: cipherSuites;
    compression_methods: Compression list;
    extensions: bytes;
  }

(* Server Hello *)

type serverHello = {
    server_version: ProtocolVersion;
    sh_random: Random;
    sh_session_id: sessionID;
    cipher_suite: cipherSuite;
    compression_method: Compression;
    neg_extensions: bytes;
  }

(* (Server and Client) Certificate *)

type certificate = { certificate_list: cert list }

(* Server Key Exchange *)
(* TODO *)

(* Certificate Request *)
type ClientCertType = bytes // of length 1, between 0 and 3
val CLT_RSA_Sign     : ClientCertType 
val CLT_DSS_Sign     : ClientCertType
val CLT_RSA_Fixed_DH : ClientCertType
val CLT_DSS_Fixed_DH : ClientCertType

(* Obsolete. Use Algorithms.hashAlg and
   following conversion functions instead *)
(*
type HashAlg =
    | HA_None = 0
    | HA_md5 = 1
    | HA_sha1 = 2
    | HA_sha224 = 3
    | HA_sha256 = 4
    | HA_sha384 = 5
    | HA_sha512 = 6
*)
val hashAlg_to_tls12enum: Algorithms.hashAlg -> int
val tls12enum_to_hashAlg: int -> Algorithms.hashAlg option

// was enum type SigAlg
type sigAlg = bytes 
val SA_anonymous: sigAlg
val SA_rsa: sigAlg
val SA_dsa: sigAlg 
val SA_ecdsa: sigAlg 

val checkSigAlg: bytes -> bool 

type SigAndHashAlg = {
    SaHA_hash: Algorithms.hashAlg;
    SaHA_signature: sigAlg; }

type certificateRequest = {
    client_certificate_type: ClientCertType list;
    signature_and_hash_algorithm: (SigAndHashAlg list) option; (* Some(x) for TLS 1.2, None for previous versions *)
    certificate_authorities: string list
    }

(* Server Hello Done *)
type serverHelloDone = bytes (* empty bitstring *)

(* Client Key Exchange *)
type preMasterSecret =
    { pms_client_version : ProtocolVersion; (* Highest version supported by the client *)
      pms_random: bytes }

type clientKeyExchange =
    | EncryptedPreMasterSecret of bytes (* encryption of PMS *)
    | ClientDHPublic (* TODO *)

(* Certificate Verify *)

type certificateVerify = bytes (* digital signature of all messages exchanged until now *)

(* Finished *)
type finished = bytes