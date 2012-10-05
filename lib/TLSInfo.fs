﻿module TLSInfo

open Bytes
open CipherSuites

type sessionID = bytes
type preRole =
    | Client
    | Server
type Role = preRole

// Client/Server randomness
type crand = bytes
type srand = bytes

type SessionInfo = {
    clientID: Cert.cert list;
    serverID: Cert.cert list;
    sessionID: sessionID;
    protocol_version: ProtocolVersion;
    cipher_suite: cipherSuite;
    compression: Compression;
    init_crand: crand;
    init_srand: srand
    }

let null_sessionInfo pv =
    { clientID = [];
      serverID = [];
      certificate_request = false;
      sessionID = [||];
      protocol_version = pv;
      cipher_suite = nullCipherSuite;
      compression = NullCompression;
      init_crand = [||]
      init_srand = [||]
      }

let isNullSessionInfo s =
  s.clientID = [] && s.serverID = [] && s.certificate_request = false && s.sessionID = [||] &&
  isNullCipherSuite s.cipher_suite && s.compression = NullCompression &&
  s.init_crand = [||] && s.init_srand = [||]

type preEpoch =
    | InitEpoch of Role * (* ourRand *) bytes
    | SuccEpoch of crand * srand * SessionInfo * preEpoch
type epoch = preEpoch

let epochSI e =
    match e with
    | InitEpoch (d,b) -> let si = null_sessionInfo SSL_3p0 in si //FIXME: fake value
    | SuccEpoch (cr,sr,si,pe) -> si

let epochSRand e =
    match e with
    | InitEpoch (d,b) -> Error.unexpectedError "[epochSRand] invoked on initial epoch."
    | SuccEpoch (cr,sr,si,pe) -> sr

let epochCRand e =
    match e with
    | InitEpoch (d,b) -> Error.unexpectedError "[epochSRand] invoked on initial epoch."
    | SuccEpoch (cr,sr,si,pe) -> cr

type ConnectionInfo = {
    role: Role;
    id_in: epoch;
    id_out: epoch}

let connectionRole ci = ci.role

let initConnection role rand =
    let ctos = InitEpoch (Client,rand) in
    let stoc = InitEpoch (Server,rand) in
    match role with
    | Client -> {role = Client; id_in = stoc; id_out = ctos}
    | Server -> {role = Server; id_in = ctos; id_out = stoc}

let nextEpoch epoch crand srand si =
    SuccEpoch (crand, srand, si, epoch )

// Application configuration
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

    (* Common *)
    safe_renegotiation: bool
    server_name: Cert.hint
    client_name: Cert.hint
            
    (* Sessions database *)
    sessionDBFileName: string
    sessionDBExpiry: TimeSpan
    }

let defaultConfig ={
    minVer = SSL_3p0
    maxVer = TLS_1p2
    ciphersuites = cipherSuites_of_nameList
                    [ TLS_RSA_WITH_AES_128_CBC_SHA;
                      TLS_RSA_WITH_3DES_EDE_CBC_SHA;
                    ]
    compressions = [ NullCompression ]

    honourHelloReq = HRPResume
    allowAnonCipherSuite = false
    request_client_certificate = false
    check_client_version_in_pms_for_old_tls = true
    
    safe_renegotiation = true
    server_name = "msr-inria.tls"
    client_name = "www.inria.fr"

    sessionDBFileName = "sessionDBFile.bin"
    sessionDBExpiry = newTimeSpan 2 0 0 0 (* two days *)
    }