module TLSExtensions

open Bytes
open Error
open TLSError
open TLSConstants
open TLSInfo

// Following types only used in handshake
type clientExtension
type serverExtension

// Client side
val prepareClientExtensions: config -> ConnectionInfo -> cVerifyData -> sessionHash option -> clientExtension list
val clientExtensionsBytes: clientExtension list -> bytes
val parseServerExtensions: bytes -> (serverExtension list) Result
val negotiateClientExtensions: clientExtension list -> serverExtension list -> bool -> cipherSuite -> negotiatedExtensions Result

// Server side
val parseClientExtensions: bytes -> cipherSuites -> (clientExtension list) Result
val negotiateServerExtensions: clientExtension list -> config -> cipherSuite -> (cVerifyData * sVerifyData) -> sessionHash option -> (serverExtension list * negotiatedExtensions)
val serverExtensionsBytes: serverExtension list -> bytes

// Extension-specific
val checkClientRenegotiationInfoExtension: config -> clientExtension list -> cVerifyData -> bool
val checkServerRenegotiationInfoExtension: config -> serverExtension list -> cVerifyData -> sVerifyData -> bool
val checkClientResumptionInfoExtension:    config -> clientExtension list -> sessionHash -> bool option
val checkServerResumptionInfoExtension:    config -> serverExtension list -> sessionHash -> bool

val hasExtendedMS: negotiatedExtensions -> bool
val hasExtendedPadding: id -> bool

// type extensionType
//
// val extensionsBytes: bool -> bytes -> bytes
// val parseExtensions: bytes -> (extensionType * bytes) list Result
// val inspect_ServerHello_extensions: (extensionType * bytes) list -> bytes -> unit Result
// val checkClientRenegotiationInfoExtension: (extensionType * bytes) list -> TLSConstants.cipherSuites -> bytes -> bool

//CF what are those doing here? relocate? 
//AP Partially relocate to TLSConstants, partially implement the mandatory signature extension, and embed them there. Maybe TODO before v1.0?
val sigHashAlgBytes: Sig.alg -> bytes
val parseSigHashAlg: bytes -> Sig.alg Result
val sigHashAlgListBytes: Sig.alg list -> bytes
val parseSigHashAlgList: bytes -> Sig.alg list Result
val default_sigHashAlg: ProtocolVersion -> cipherSuite -> Sig.alg list
val sigHashAlg_contains: Sig.alg list -> Sig.alg -> bool
val cert_type_list_to_SigHashAlg: certType list -> ProtocolVersion -> Sig.alg list
val cert_type_list_to_SigAlg: certType list -> sigAlg list
val sigHashAlg_bySigList: Sig.alg list -> sigAlg list -> Sig.alg list