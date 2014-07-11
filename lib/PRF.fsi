﻿#light "off"

module PRF

open Bytes
open TLSConstants
open TLSInfo

type repr = bytes
type ms
type masterSecret = ms

#if ideal
val sample: msId -> ms
#endif

//#begin-coerce
val coerce: msId -> repr -> ms
//#end-coerce

val keyCommit: csrands -> ProtocolVersion -> aeAlg -> negotiatedExtensions -> unit
val keyGenClient: id -> id -> ms -> StatefulLHAE.writer * StatefulLHAE.reader
val keyGenServer: id -> id -> ms -> StatefulLHAE.writer * StatefulLHAE.reader

val makeVerifyData:  SessionInfo -> ms -> Role -> bytes -> bytes 
val checkVerifyData: SessionInfo -> ms -> Role -> bytes -> bytes -> bool

val ssl_certificate_verify: SessionInfo -> ms -> TLSConstants.sigAlg -> bytes -> bytes

