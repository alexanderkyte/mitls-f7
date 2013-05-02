module PRF

open Bytes
open TLSConstants
open TLSInfo

val prfAlg: SessionInfo -> prfAlg  
 
type msIndex =  
  PMS.pms * 
  csrands *                                          
  prfAlg  
   
#if ideal
val safeMS_msIndex: msIndex -> bool  
#endif

type repr = bytes
type ms

#if ideal
type masterSecret = msIndex * ms
#else
type masterSecret = ms
#endif

#if ideal
val sample: SessionInfo -> PMS.pms -> masterSecret
#endif

//#begin-coerce
val coerce: SessionInfo -> PMS.pms -> repr -> masterSecret
//#end-coerce

val keyGen: ConnectionInfo -> masterSecret -> StatefulLHAE.writer * StatefulLHAE.reader

val makeVerifyData:  SessionInfo -> masterSecret -> Role -> bytes -> bytes 
val checkVerifyData: SessionInfo -> masterSecret -> Role -> bytes -> bytes -> bool

val ssl_certificate_verify: SessionInfo -> masterSecret -> TLSConstants.sigAlg -> bytes -> bytes

