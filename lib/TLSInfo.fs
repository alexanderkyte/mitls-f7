﻿module TLSInfo

open Data
open Bytearray
open Principal
open HS_ciphersuites

type sessionID = bytes

type prerole =
    | ClientRole
    | ServerRole

type role = prerole

type SessionInfo = {
    role: role
    clientID: pri_cert option
    serverID: pri_cert option
    sessionID: sessionID option
    protocol_version: ProtocolVersionType
    cipher_suite: cipherSuite
    compression: Compression
    init_crand: bytes
    init_srand: bytes
    }

let init_sessionInfo role =
    { role = role;
      clientID = None;
      serverID = None;
      sessionID = None;
      protocol_version = ProtocolVersionType.UnknownPV;
      cipher_suite = nullCipherSuite;
      compression = Null;
      init_crand = empty_bstr
      init_srand = empty_bstr
      }

type KeyInfo = {
    sinfo: SessionInfo
    crand: bytes
    srand: bytes
    }