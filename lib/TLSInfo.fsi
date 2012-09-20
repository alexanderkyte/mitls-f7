﻿module TLSInfo

open Bytes
open Algorithms
open Certificate
open CipherSuites

type sessionID = bytes
type preRole =
    | Client
    | Server
type Role = preRole

// Client/Server randomness
type crand = bytes
type srand = bytes
// VerifyData Payload, for safe renego extension
type cVerifyData = bytes
type sVerifyData = bytes

type SessionInfo = {
    clientID: cert option;
    serverID: cert option;
    sessionID: sessionID;
    protocol_version: ProtocolVersion;
    cipher_suite: cipherSuite;
    compression: Compression;
    init_crand: crand;
    init_srand: srand
    }

type preEpoch
type epoch = preEpoch

val epochSI: epoch -> SessionInfo
val epochSRand: epoch -> srand
val epochCRand: epoch -> crand
val epochCVerifyData: epoch -> cVerifyData
val epochSVerifyData: epoch -> sVerifyData

//type epoch = {
//    sinfo: SessionInfo;
//    dir: Direction;
//    crand: bytes;
//    srand: bytes;
//    (* cVerifyData: bytes
//    sVerifyData: bytes *)
//    }

// Role is of the writer
type ConnectionInfo =
    { role: Role;
      id_in:  epoch;
      id_out: epoch}
val connectionRole: ConnectionInfo -> Role

val null_sessionInfo: ProtocolVersion -> SessionInfo
val isNullSessionInfo: SessionInfo -> bool
val initConnection: Role -> bytes -> ConnectionInfo
val nextEpoch: epoch -> crand -> srand -> cVerifyData -> sVerifyData -> SessionInfo -> epoch
//val dual_KeyInfo: epoch -> epoch

// Application configuration options
type helloReqPolicy =
    | HRPIgnore
    | HRPFull
    | HRPResume

type config = {
    minVer: ProtocolVersion
    maxVer: ProtocolVersion
    ciphersuites: cipherSuites
    compressions: Compression list

    (* Handshake specific options *)
    (* Client side *)
    honourHelloReq: helloReqPolicy
    allowAnonCipherSuite: bool
    (* Server side *)
    request_client_certificate: bool
    check_client_version_in_pms_for_old_tls: bool
    server_cert_file: string (* FIXME: certificates should be found in a better way. To be fixed *)
    (* Common *)
    trustedRootCertificates: cert list
    safe_renegotiation: bool

    (* Sessions database *)
    sessionDBFileName: string
    sessionDBExpiry: TimeSpan
    }

val defaultConfig: config