﻿#light "off"

module FlexTLS.FlexTypes

open Bytes
open TLSInfo
open TLSConstants
open TLSExtensions




/// <summary>
/// Fragmentation policy union type,
/// The constructor represents the number of fragments that will be sent to the network
/// The value represents the length of the fragments that will be sent
/// </summary>
type fragmentationPolicy =
    /// <summary> Will send All fragments, each of length LEN bytes </summary>
    | All of int
    /// <summary> Will send One fragment of length LEN bytes </summary>
    | One of int

/// <summary>
/// DH key exchange parameters record,
/// Contains both public and secret values associated to Diffie Hellman parameters
/// </summary>
type kexDH = { 
    /// <summary> Tuple (p,g) that contains both p and g public DH parameters </summary>
    pg: bytes * bytes;
    /// <summary> Local secret value of the DH exchange </summary>
    x:  bytes;
    /// <summary> Local public value (g^x mod p) of the DH exchange </summary>
    gx: bytes;
    /// <summary> Peer's public value (g^y mod p) of the DH exchange </summary>
    gy: bytes
}

/// <summary>
/// DH key exchange parameters record, for negotiated DH parameters
/// Contains both public and secret values associated of Diffie Hellman parameters
/// </summary>
type kexDHTLS13 = { 
    /// <summary> Negotiated DH group </summary>
    group: dhGroup;
    /// <summary> Local secret value of the DH exchange </summary>
    x:  bytes;
    /// <summary> Local public value (g^x mod p) of the DH exchange </summary>
    gx: bytes;
    /// <summary> Peer's public value (g^y mod p) of the DH exchange </summary>
    gy: bytes;
}

/// <summary>
/// Key exchange union type,
/// The constructor represents the type of Key Exchange Mechanism used in the Handshake
/// </summary>
type kex =
    /// <summary> Key Exchange Type is RSA and the constructor holds the pre-master secret </summary>
    | RSA of bytes
    /// <summary> Key Exchange Type is Diffie-Hellman and the constructor holds all DH parameters </summary>
    | DH of kexDH
    /// <summary> Key Exchange Type is Diffie-Hellman with negotiated group and the constructor holds all DH parameters </summary>
    | DH13 of kexDHTLS13
 // | ECDH of kexECDH // TODO

/// <summary>
/// Handshake Message record type for Client Key Share
/// </summary>
type FClientKeyShare = {
    /// <summary> List of Key Exchange offers </summary>
    offers:list<HandshakeMessages.tls13kex>;
    /// <summary> Message bytes </summary>
    payload:bytes;
}

/// <summary>
/// Handshake Message record type for Server Key Share
/// </summary>
type FServerKeyShare = {
    /// <summary> Key Exchange offer </summary>
    kex:HandshakeMessages.tls13kex;
    /// <summary> Message bytes </summary>
    payload:bytes;
}

/// <summary>
/// Session Keys record,
/// This structure contains all secret information of a Handshake
/// </summary>
type keys = {
    /// <summary> Key Exchange bytes </summary>
    kex: kex;
    /// <summary> Pre Master Secret bytes </summary>
    pms: bytes;
    /// <summary> Master Secret bytes </summary>
    ms: bytes;
    /// <summary> Keys bytes of an epoch, as a tuple (reading keys, writing keys) </summary>
    epoch_keys: bytes * bytes;
             (* read  , write *)
}

/// <summary>
/// Channel record,
/// Keeps track of the Record state and the associated Epoch of an I/O channel
/// </summary>
/// <remarks> There is no CCS buffer because those are only one byte </remarks>
type channel = {
    /// <summary> Secret and mutable state of the current epoch (keys, sequence number, etc...) </summary>
    record: Record.ConnectionState;
    /// <summary> Public immutable data of the current epoch </summary>
    epoch:  TLSInfo.epoch;
    /// <summary> Raw bytes of the keys currently in use. This is meant to be a read-only (informational) field: changes to this field will have no effect </summary>
    keys: keys;
    /// <summary> Initially chosen protocol version before negotiation </summary>
    epoch_init_pv: ProtocolVersion;
    /// <summary> Verify data of the channel </summary>
    verify_data: option<bytes>;
    /// <summary> Buffer for messages of the Handshake content type </summary>
    hs_buffer: bytes;
    /// <summary> Buffer for messages of the Alert content type </summary>
    alert_buffer: bytes;
    /// <summary> Buffer for messages of the ApplicationData content type </summary>
    appdata_buffer: bytes
}

/// <summary>
/// Global state of the application
/// </summary>
type state = {
    /// <summary> Reading channel (Incoming) </summary>
    read: channel;
    /// <summary> Writing channel (Outcoming) </summary>
    write: channel;
    /// <summary> Network stream where the data is exchanged with the peer </summary>
    ns: Tcp.NetworkStream;
}

/// <summary>
/// Next security context record used to generate a new epoch
/// </summary>
type nextSecurityContext = {
    /// <summary> Next session information (for the future epoch/record state) </summary>
    si: SessionInfo;
    /// <summary> Most recent client random; used to generate new keys </summary>
    crand: bytes;
    /// <summary> Most recent server random; used to generate new keys </summary>
    srand: bytes;
    /// <summary> Keys to be used by the next epoch </summary>
    keys: keys;
    /// <summary> Offers of DH groups and public keys from the client (useful for negotiated DH groups, and hence for TLS 1.3) </summary>
    offers: list<kex>;
}

/// <summary>
/// Handshake Message record type for Hello Request
/// </summary>
type FHelloRequest = {
    /// <summary> Message Bytes </summary>
    payload: bytes;
}

/// <summary>
/// Handshake Message record type for Client Hello
/// </summary>
type FClientHello = {
    /// <summary> Protocol version </summary>
    pv: ProtocolVersion;
    /// <summary> Client random bytes </summary>
    rand: bytes;
    /// <summary> Session identifier. A non-empty byte array indicates that the client wants resumption </summary>
    sid: bytes;
    /// <summary> List of ciphersuite names supported by the client </summary>
    ciphersuites: list<cipherSuiteName>;
    /// <summary> List of compression mechanisms supported by the client </summary>
    comps: list<Compression>;
    /// <summary> List of extensions proposed by the client; None: user asks for default; Some<list>: user gives value. A returned client hello always has Some<list>. </summary>
    ext: option<list<clientExtension>>;
    /// <summary> Message Bytes </summary>
    payload: bytes;
}

/// <summary>
/// Handshake Message record type for Server Hello
/// </summary>
type FServerHello = {
/// <summary> Protocol version </summary>
    pv: option<ProtocolVersion>;
    /// <summary> Server random bytes </summary>
    rand: bytes;
    /// <summary> Session identifier. A non-empty byte array indicates that the server accepted resumption </summary>
    sid: option<bytes>;
    /// <summary> Ciphersuite selected by the server </summary>
    ciphersuite: option<cipherSuiteName>;
    /// <summary> Compression selected by the server </summary>
    comp: Compression;
    /// <summary> List of extensions agreed by the server </summary>
    ext: option<list<serverExtension>>;
    /// <summary> Message bytes </summary>
    payload: bytes;
}

/// <summary>
/// Handshake Message record type for Certificate
/// </summary>
type FCertificate = {
    /// <summary> Full certificate chain bytes </summary>
    chain: Cert.chain;
    /// <summary> Message bytes</summary>
    payload: bytes;
}

/// <summary>
/// Handshake Message record type for Server Key Exchange
/// </summary>
type FServerKeyExchange = {
    /// <summary> Signature algorithm </summary>
    sigAlg: Sig.alg;
    /// <summary> Signature </summary>
    signature: bytes;
    /// <summary> Key Exchange Information </summary>
    kex: kex;
    /// <summary> Message bytes </summary>
    payload: bytes;
}

/// <summary>
/// Handshake Message record type for Certificate Request
/// </summary>
type FCertificateRequest = {
    /// <summary> List of certificate types </summary>
    certTypes: list<certType>;
    /// <summary> List of Signature algorithms </summary>
    sigAlgs: list<Sig.alg>;
    /// <summary> List of user provided cert names </summary>
    names: list<string>;
    /// <summary> Message bytes </summary>
    payload: bytes;
}

/// <summary>
/// Handshake Message record type for Server Hello Done
/// </summary>
type FServerHelloDone = {
    /// <summary> Message Bytes</summary>
    payload: bytes;
}

/// <summary>
/// Handshake Message record type for Certificate Verify
/// </summary>
type FCertificateVerify = {
    /// <summary> Signature algorithm </summary>
    sigAlg: Sig.alg;
    /// <summary> Signature </summary>
    signature: bytes;
    /// <summary> Message bytes </summary>
    payload: bytes;
}

/// <summary>
/// Handshake Message record type for Client Key Exchange
/// </summary>
type FClientKeyExchange = {
    /// <summary> Key Exchange mechanism information </summary>
    kex:kex;
    /// <summary> Message bytes </summary>
    payload:bytes;
}


/// <summary>
/// CCS Message record type
/// </summary>
type FChangeCipherSpecs = {
    /// <summary> Message bytes </summary>
    payload: bytes;
}

/// <summary>
/// Handshake Message record type for Finished
/// </summary>
type FFinished = {
    /// <summary> Typically PRF(ms,hash(handshake log)) </summary>
    verify_data: bytes;
    /// <summary> Message bytes </summary>
    payload: bytes;
}
