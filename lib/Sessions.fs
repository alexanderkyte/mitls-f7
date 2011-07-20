﻿module Sessions

open Bytearray
open Data
open Formats
open HS_ciphersuites
open Principal

type prerole =
    | ClientRole
    | ServerRole

type role = prerole

type sessionID = bytes

type SessionMoreInfo = {
    mi_protocol_version: ProtocolVersionType
    mi_cipher_suite: CipherSuite
    mi_compression: Compression
    mi_pms: bytes
    }

type SessionInfo = {
    role: role;
    clientID: pri_cert option;
    serverID: pri_cert option;
    sessionID: sessionID option
    more_info: SessionMoreInfo
    }

let init_sessionInfo role =
    { role = role;
      clientID = None;
      serverID = None;
      sessionID = None;
      more_info =
        {
        mi_protocol_version = ProtocolVersionType.UnknownPV;
        mi_cipher_suite = TLS_NULL_WITH_NULL_NULL;
        mi_compression = Null;
        mi_pms = empty_bstr
        }
      }