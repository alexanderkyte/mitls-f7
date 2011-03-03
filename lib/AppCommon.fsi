﻿module AppCommon

open Formats
open HS_ciphersuites

type protocolOptions = {
    minVer: ProtocolVersionType
    maxVer: ProtocolVersionType
    ciphersuites: cipherSuites
    compressions: Compression list
    }

val defaultProtocolOptions: protocolOptions

val max_TLSPlaintext_fragment_length: int
val fragmentLength: int