module PRF

open Bytes
open TLSConstants
open TLSInfo


   
#if ideal
val safeMS_msIndex: msId -> bool  
#endif

type repr = bytes
type ms
type masterSecret = ms

#if ideal
val sample: SessionInfo -> masterSecret
#endif

//#begin-coerce
val coerce: SessionInfo -> repr -> masterSecret
//#end-coerce

val keyCommit: csrands -> aeAlg -> unit
val keyGenClient: ConnectionInfo -> masterSecret -> StatefulLHAE.writer * StatefulLHAE.reader
val keyGenServer: ConnectionInfo -> masterSecret -> StatefulLHAE.writer * StatefulLHAE.reader

val makeVerifyData:  SessionInfo -> masterSecret -> Role -> bytes -> bytes 
val checkVerifyData: SessionInfo -> masterSecret -> Role -> bytes -> bytes -> bool

val ssl_certificate_verify: SessionInfo -> masterSecret -> TLSConstants.sigAlg -> bytes -> bytes

