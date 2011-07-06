﻿module AppCommon

open Formats
open HS_ciphersuites

type helloReqPolicy =
    | HRPIgnore
    | HRPFull
    | HRPResume

type protocolOptions = {
    minVer: ProtocolVersionType
    maxVer: ProtocolVersionType
    ciphersuites: cipherSuites
    compressions: Compression list

    (* Handshake specific options *)
    honourHelloReq: helloReqPolicy
    }

let defaultProtocolOptions ={
    minVer = SSL_3p0
    maxVer = TLS_1p2
    ciphersuites = [ TLS_RSA_WITH_AES_128_CBC_SHA;
                    TLS_RSA_WITH_RC4_128_MD5;
                    TLS_RSA_WITH_RC4_128_SHA;       
                    TLS_RSA_WITH_DES_CBC_SHA;
                    TLS_NULL_WITH_NULL_NULL;
                    TLS_RSA_WITH_NULL_MD5;               
                    TLS_RSA_WITH_NULL_SHA;
                  ]
    compressions = [ Null ]

    honourHelloReq = HRPResume
    }

let max_TLSPlaintext_fragment_length = 1<<<14 (* just a reminder *)
let fragmentLength = max_TLSPlaintext_fragment_length (*1*)