﻿(* Handshake protocol *) 
module Handshake

open Bytes
open Error
open Formats
open Algorithms
open CipherSuites
open TLSInfo
open SessionDB
open PRFs
open DataStream

// BEGIN HS_msg

// This section is from the legacy HS_msg module, now merged with Handshake. 
// Still, there are some redundancies that should be eliminated, 
// by semantically merge the two.

(*** Following RFC5246 A.4 *)

type HandshakeType =
    | HT_hello_request
    | HT_client_hello
    | HT_server_hello
    | HT_certificate
    | HT_server_key_exchange
    | HT_certificate_request
    | HT_server_hello_done
    | HT_certificate_verify
    | HT_client_key_exchange
    | HT_finished
    | HT_unknown of byte //$

let htbytes t =
    match t with
    | HT_hello_request       -> [|  0uy |] 
    | HT_client_hello        -> [|  1uy |]
    | HT_server_hello        -> [|  2uy |]
    | HT_certificate         -> [| 11uy |]
    | HT_server_key_exchange -> [| 12uy |]
    | HT_certificate_request -> [| 13uy |]
    | HT_server_hello_done   -> [| 14uy |]
    | HT_certificate_verify  -> [| 15uy |]
    | HT_client_key_exchange -> [| 16uy |]
    | HT_finished            -> [| 20uy |]
    | HT_unknown x           -> unexpectedError "Unknown handshake type"

let parseHT (b:bytes) = 
    match b.[0] with
    |  0uy -> HT_hello_request
    |  1uy -> HT_client_hello
    |  2uy -> HT_server_hello
    | 11uy -> HT_certificate
    | 12uy -> HT_server_key_exchange
    | 13uy -> HT_certificate_request
    | 14uy -> HT_server_hello_done
    | 15uy -> HT_certificate_verify
    | 16uy -> HT_client_key_exchange
    | 20uy -> HT_finished
    |  x   -> HT_unknown (x)

// missing Handshake and its generic formatting
// := HandshakeType(ht) @| VLBytes(3,body) 


(** A.4.1 Hello Messages *)

type helloRequest = bytes  // empty bitstring 

// missing SessionID, defined in TLSInfo
// missing CompressionMethod

// missing some details, e.g. ExtensionType/Data
type Extension =
    | HExt_renegotiation_info
    | HExt_unknown of bytes

let bytes_of_HExt hExt =
    match hExt with
    | HExt_renegotiation_info -> [|0xFFuy; 0x01uy|]
    | HExt_unknown (_)        -> unexpectedError "Unknown extension type"

let hExt_of_bytes b =
    match b with
    | [|0xFFuy; 0x01uy|] -> HExt_renegotiation_info
    | _                  -> HExt_unknown b

type clientHello = {
    ch_client_version     : ProtocolVersion;
    ch_random             : bytes;
    ch_session_id         : sessionID;
    ch_cipher_suites      : cipherSuites;
    ch_compression_methods: Compression list;
    ch_extensions         : bytes; //$ why unparsed? 
  }

type serverHello = {
    sh_server_version     : ProtocolVersion;
    sh_random             : bytes;
    sh_session_id         : sessionID;
    sh_cipher_suite       : cipherSuite;
    sh_compression_method : Compression;
    sh_neg_extensions     : bytes;
  }

let hashAlg_to_tls12enum ha =
    match ha with
    | Algorithms.MD5    -> [| 1uy |]
    | Algorithms.SHA    -> [| 2uy |]
    | Algorithms.SHA256 -> [| 4uy |]
    | Algorithms.SHA384 -> [| 5uy |]

let tls12enum_to_hashAlg n =
    match n with
    | [| 1uy |] -> Some Algorithms.MD5
    | [| 2uy |] -> Some Algorithms.SHA
    | [| 4uy |] -> Some Algorithms.SHA256
    | [| 5uy |] -> Some Algorithms.SHA384
    | _         -> None

type sigAlg = bytes

let SA_anonymous = [| 0uy |]
let SA_rsa       = [| 1uy |]
let SA_dsa       = [| 2uy |]
let SA_ecdsa     = [| 3uy |] 

let checkSigAlg v =  
    v = SA_anonymous 
 || v = SA_rsa 
 || v = SA_dsa
 || v = SA_ecdsa 

type SigAndHashAlg = {
    SaHA_hash: Algorithms.hashAlg;
    SaHA_signature: sigAlg;
    }


(** A.4.2 Server Authentication and Key Exchange Messages *)

//useless?
// type certificate = { certificate_list: cert list }

(* Server Key Exchange *)
(* TODO *)

(* Certificate Request *)

type certificateRequest = {
    (* RFC acknowledges the relation between these fields is "somewhat complicated" *)
    client_certificate_type: Cert.certType list
    signature_and_hash_algorithm: (SigAndHashAlg list) option (* Some(x) for TLS 1.2, None for previous versions *)
    certificate_authorities: string list
    }

type serverHelloDone = bytes // empty bitstring


(** A.4.3 Client Authentication and Key Exchange Messages *) 

type preMasterSecret =
    { pms_client_version: ProtocolVersion; (* highest version supported by the client *)
      pms_random: bytes }

type clientKeyExchange =
    | EncryptedPreMasterSecret of bytes (* encryption of PMS *)
    | ClientDHPublic (* TODO *)

(* Certificate Verify *)

type certificateVerify = bytes (* digital signature of all messages exchanged until now *)


(** A.4.4 Handshake Finalization Message *)

type finished = bytes

// END HS_msg

// Handshake module

// Handshake state machines 

// Legacy state machine. Here for reference until the new one is stable.

// type clientState =
//   | ServerHello (* of SessionInfo Option 
//                    client proposed session to be resumed, useful to
//                    check whether we're going to do resumption or full
//                    negotiation *)
//   | Certificate       (* of SessionInfo (* being established *) *)
//   | ServerKeyExchange (* of SessionInfo (* begin established *) *)
//   | CertReqOrSHDone   (* of SessionInfo (* being established *) *)
//   | CSHDone of           (* SessionInfo * *) clientSpecificState  
//   | CCCS of              (* SessionInfo * *) clientSpecificState      
//   | CFinished of         (* SessionInfo * *) clientSpecificState 
//   | CWaitingToWrite of   (* SessionInfo * *) clientSpecificState
//   | CIdle
// 
// type serverState = (* should also include SessionInfo beging established? *)
//   | ClientHello
//   | ClCert of serverSpecificState
//   | ClientKEX of serverSpecificState
//   | CertificateVerify of serverSpecificState
//   | SCCS of serverSpecificState
//   | SFinished of serverSpecificState
//   | SWaitingToWrite of serverSpecificState
//   | SIdle

// New state machine begins
type log = bytes

type serverState =  (* note that the CertRequest bits are determined by the config *) 
                     (* we may omit some ProtocolVersion, mostly a ghost variable *)
   | ClientHello
   | ClientCertificateRSA    of SessionInfo * ProtocolVersion * log 
   | ClientCertificateDH     of SessionInfo * log 
   | ClientCertificateDHE    of SessionInfo * (* DHE.sk * *) log 
   | ClientKeyExchangeRSA    of SessionInfo * ProtocolVersion * log
   | ClientKeyExchangeDH     of SessionInfo * log 
   | ClientKeyExchangeDHE    of SessionInfo * (* DHE.sk * *) log 
   | CertificateVerify       of SessionInfo * masterSecret * log 
   | ClientCCS               of SessionInfo * masterSecret * log
   | ClientFinished          of SessionInfo * masterSecret * epoch * StatefulAEAD.writer
   (* by convention, the parameters are named si, cv, cr', sr', ms, log *)
   | ServerWritingCCS        of SessionInfo * masterSecret * epoch * StatefulAEAD.writer
   | ServerWritingFinished   of SessionInfo * masterSecret
   | ServerWritingCCSResume  of ConnectionInfo * StatefulAEAD.writer * StatefulAEAD.reader
   | ClientCCSResume         of epoch * StatefulAEAD.reader
   | ClientFinishedResume
   | ServerIdle
   (* the ProtocolVersion is the highest TLS version proposed by the client *)

type clientState = 
   | ServerHello            of crand * sessionID (* * bytes for extensions? *) * log
   | ServerCertificateRSA   of SessionInfo * log
   | ServerCertificateDH    of SessionInfo * log
   | ServerCertificateDHE   of SessionInfo * log
   | ServerKeyExchangeDHE   of SessionInfo * log
   | ServerKeyExchangeDH_anon of SessionInfo * log (* Not supported yet *)
   | CertificateRequestRSA  of SessionInfo * log (* In fact, CertReq or SHelloDone will be accepted *)
   | CertificateRequestDH   of SessionInfo * log (* We pick our cert and store it in sessionInfo as soon as the server requests it.
                                                    We put None if we don't have such a certificate, and we know whether to send
                                                    the Certificate message or not based on the state when we receive the Finished message *)
   | CertificateRequestDHE  of SessionInfo * (* DHE.sk * *) log
   | ServerHelloDoneRSA     of SessionInfo * log
   | ServerHelloDoneDH      of SessionInfo * log
   | ServerHelloDoneDHE     of SessionInfo * (* DHE.sk * *) bytes * log
   | ClientWritingCCS       of SessionInfo * masterSecret * log
   | ServerCCS              of SessionInfo * masterSecret * epoch * StatefulAEAD.reader
   | ServerFinished         of SessionInfo * masterSecret
   | ServerCCSResume        of ConnectionInfo * StatefulAEAD.writer * StatefulAEAD.reader
   | ServerFinishedResume   of epoch * StatefulAEAD.writer
   | ClientWritingCCSResume of epoch * StatefulAEAD.writer
   | ClientWritingFinishedResume
   | ClientIdle

type protoState = // Cannot use Client and Server, otherwise clashes with Role
  | PSClient of clientState
  | PSServer of serverState

type KIAndCCS = (epoch * StatefulAEAD.state)

type pre_hs_state = {
  (* I/O buffers *)
  hs_outgoing    : bytes;                  (* outgoing data before a ccs *)
  //ccs_outgoing: (bytes * KIAndCCS) option; (* marker telling there is a ccs ready *)
  //hs_outgoing_after_ccs: bytes;            (* data to be sent after the ccs has been sent *)
  hs_incoming    : bytes;                  (* partial incoming HS message *)
  //ccs_incoming: KIAndCCS option; (* used to store the computed secrets for receiving data. Not set when receiving CCS, but when we compute the session secrects *)
 
  (* local configuration *)
  poptions: config; 
  sDB: SessionDB;
  
  (* current handshake & session we are establishing, to be pushed within pstate *) 
  pstate: protoState;
  //hs_next_info: SessionInfo; (* session being established or resumed, including crand and srand from its full handshake *)
  //next_ms: masterSecret;     (* master secret being established *)
  //hs_msg_log: bytes;         (* sequence of HS messages sent & received so far, to be eventually authenticated *) 
  
  (* to be pushed only in resumption-specific state: *)
  //ki_crand: bytes;           (* fresh client random for the session being established *)
  //ki_srand: bytes;           (* fresh server random for the session being established *)

  (* state specific to the renegotiation-info extension
     - exchanged in the extended Hello messages 
     - updated with the content of the verifyData messages as the handshake completes *)
  // We'll retrieve them from the current epoch
  //hs_renegotiation_info_cVerifyData: bytes 
  //hs_renegotiation_info_sVerifyData: bytes 
}

type hs_state = pre_hs_state
type nextState = hs_state

/// Handshake message format 

let makeMessage ht data = htbytes ht @| vlbytes 3 data 

let CCSBytes = [| 1uy |]

let parseMessage state =
    (* Inefficient but simple implementation:
       we repeatedly parse the whole incoming buffer until we have a complete message;
       we then remove that message from the incoming buffer. *)
    if length state.hs_incoming < 4 then None (* not enough data to start parsing *)
    else
        let (hstypeb,rem) = Bytes.split state.hs_incoming 1 in
        let (lenb,rem) = Bytes.split rem 3 in
        let len = int_of_bytes lenb in
        if length rem < len then None (* not enough payload, try next time *)
        else
            let hstype = parseHT hstypeb in
            let (payload,rem) = Bytes.split rem len in
            let state = { state with hs_incoming = rem } in
            let to_log = hstypeb @| lenb @| payload in //$
            Some(state,hstype,payload,to_log)

/// Hello Request 

let makeHelloRequestBytes () = makeMessage HT_hello_request [||]

/// Extensions

let makeExtStructBytes extType data =
    let extBytes = bytes_of_HExt extType in
    let payload = vlbytes 2 data in
    extBytes @| payload

let makeExtBytes data =  vlbytes 2 data

let makeRenegExtBytes verifyData =
    let payload = vlbytes 1 verifyData in
    makeExtStructBytes HExt_renegotiation_info payload

let rec extensionList_of_bytes_int data list =
    match length data with
    | 0 -> correct (list)
    | x when x < 4 ->
        (* This is a parsing error, or a malformed extension *)
        Error (HSError(AD_decode_error), HSSendAlert)
    | _ ->
        let (extTypeBytes,rem) = Bytes.split data 2 in
        let extType = hExt_of_bytes extTypeBytes in
        match vlsplit 2 rem with
        | Error(x,y) -> Error (HSError(AD_decode_error), HSSendAlert) (* Parsing error *)
        | Correct (payload,rem) -> extensionList_of_bytes_int rem ([(extType,payload)] @ list)

let extensionList_of_bytes data =
    match length data with
    | 0 -> correct ([])
    | 1 -> Error(HSError(AD_decode_error),HSSendAlert)
    | _ ->
        match vlparse 2 data with
        | Error(x,y)    -> Error(HSError(AD_decode_error),HSSendAlert)
        | Correct(exts) -> extensionList_of_bytes_int exts []

let check_reneg_info payload expected =
    // We also check there were no more data in this extension.
    match vlparse 1 payload with
    | Error(x,y)     -> false
    | Correct (recv) -> equalBytes recv expected

let check_client_renegotiation_info cHello expected =
    match extensionList_of_bytes cHello.ch_extensions with
    | Error(x,y) -> false
    | Correct(extList) ->
        (* Check there is at most one renegotiation_info extension *)
        let ren_ext_list = List.filter (fun (ext,_) -> ext = HExt_renegotiation_info) extList in
        if ren_ext_list.Length > 1 then
            false
        else
            let has_SCSV = contains_TLS_EMPTY_RENEGOTIATION_INFO_SCSV cHello.ch_cipher_suites in
            if equalBytes expected [||] 
            then  
                (* First handshake *)
                if ren_ext_list.Length = 0 
                then has_SCSV
                    (* either client gave SCSV and no extension; this is OK for first handshake *)
                    (* or the client doesn't support this extension and we fail *)
                else
                    let ren_ext = ren_ext_list.Head in
                    let (extType,payload) = ren_ext in
                    check_reneg_info payload expected
            else
                (* Not first handshake *)
                if has_SCSV || (ren_ext_list.Length = 0) then false
                else
                    let ren_ext = ren_ext_list.Head in
                    let (extType,payload) = ren_ext in
                    check_reneg_info payload expected

let inspect_ServerHello_extensions recvExt expected =
    (* Code is ad-hoc for the only extension we support now: renegotiation_info *)
    match extensionList_of_bytes recvExt with
    | Error (x,y) -> Error (x,y)
    | Correct (extList) ->
        (* We expect to find exactly one extension *)
        match extList.Length with
        | 0 -> Error(HSError(AD_handshake_failure),HSSendAlert)
        | x when not (x = 1) -> Error(HSError(AD_unsupported_extension),HSSendAlert)
        | _ ->
            let (extType,payload) = extList.Head in
            match extType with
            | HExt_renegotiation_info ->
                (* Check its content *)
                if check_reneg_info payload expected then
                    let unitVal = () in
                    correct (unitVal)
                else
                    (* RFC 5746, sec 3.4: send a handshake failure alert *)
                    Error(HSError(AD_handshake_failure),HSSendAlert)
            | _ -> Error(HSError(AD_unsupported_extension),HSSendAlert)


/// Client and Server random values

let makeRandom() = //$ crypto abstraction? timing guarantees local disjointness
    let time = makeTimestamp () in
    let timeb = bytes_of_int 4 time in
    let rnd = mkRandom 28 in
    timeb @| rnd

/// Compression algorithms 

let rec compressionMethodsBytes cs =
   match cs with
   | c::cs -> compressionBytes c @| compressionMethodsBytes cs
   | []    -> [||] 

/// Client Hello 

let parseClientHello data =
    // pre: Length(data) > 34
    // correct post: something like data = ClientHelloBytes(...) 
    let (clVerBytes,cr,data) = split2 data 2 32 in
    match parseVersion clVerBytes with
    | Error(x,y) -> Error(x,y)
    | Correct(cv) ->
    match vlsplit 1 data with
    | Error(x,y) -> Error(x,y)
    | Correct (sid,data) ->
    match vlsplit 2 data with
    | Error(x,y) -> Error(x,y)
    | Correct (clCiphsuitesBytes,data) ->
    match parseCipherSuites clCiphsuitesBytes with
    | Error(x,y) -> Error(x,y) 
    | Correct(clientCipherSuites) ->
    match vlsplit 1 data with
    | Error(x,y) -> Error(x,y)
    | Correct (cmBytes,extensions) ->
    let cm = parseCompressions cmBytes
    correct(
     { ch_client_version      = cv 
       ch_random              = cr 
       ch_session_id          = sid
       ch_cipher_suites       = clientCipherSuites
       ch_compression_methods = cm 
       ch_extensions          = extensions})

// called only just below; inline? clientHello record seems unhelpful
// AP: Two (useless) indirection levels. We should get rid of the struct here.
let makeClientHello poptions crand session prevCVerifyData =
    let ext =
        if poptions.safe_renegotiation 
        then makeExtBytes (makeRenegExtBytes prevCVerifyData)
        else [||]
    { ch_client_version = poptions.maxVer
      ch_random = crand
      ch_session_id = session
      ch_cipher_suites = poptions.ciphersuites
      ch_compression_methods = poptions.compressions
      ch_extensions = ext }

let makeClientHelloBytes poptions crand session cVerifyData =
    let cHello     = makeClientHello poptions crand session cVerifyData in
    let cVerB      = versionBytes cHello.ch_client_version in
    let random     = cHello.ch_random in
    let csessB     = vlbytes 1 cHello.ch_session_id in
    let ccsuitesB  = vlbytes 2 (bytes_of_cipherSuites cHello.ch_cipher_suites)
    let ccompmethB = vlbytes 1 (compressionMethodsBytes cHello.ch_compression_methods) 
    let data = cVerB @| random @| csessB @| ccsuitesB @| ccompmethB @| cHello.ch_extensions in
    makeMessage HT_client_hello data

/// Server Hello 

let makeServerHelloBytes sinfo srand ext = 
    let verB = versionBytes sinfo.protocol_version in
    let sidB = vlbytes 1 sinfo.sessionID
    let csB = cipherSuiteBytes sinfo.cipher_suite in
    let cmB = compressionBytes sinfo.compression in
    let data = verB @| srand @| sidB @| csB @| cmB @| ext in
    makeMessage HT_server_hello data

let parseServerHello data =
    let (serverVerBytes,serverRandomBytes,data) = split2 data 2 32 
    match parseVersion serverVerBytes with
    | Error(x,y) -> Error(x,y)
    | Correct(serverVer) ->
    match vlsplit 1 data with
    | Error(x,y) -> Error (x,y)
    | Correct (sid,data) ->
    let (csBytes,cmBytes,data) = split2 data 2 1 
    match cipherSuite_of_bytes csBytes with
    | Error(x,y) -> Error(x,y)
    | Correct(cs) ->
    match parseCompression cmBytes with
    | Error(x,y) -> Error(x,y)
    | Correct(cm) ->
    let r = 
     { sh_server_version = serverVer
       sh_random = serverRandomBytes
       sh_session_id = sid
       sh_cipher_suite = cs
       sh_compression_method = cm
       sh_neg_extensions = data}
    correct(r)


/// Initiating Handshakes, mostly on the client side. 

let init (role:Role) poptions =
    (* Start a new first session without resumption *)
    let sid = [||] in
    let rand = makeRandom() in
    let ci = initConnection role rand in
    let cVerifyData = epochCVerifyData ci.id_out in // in fact [||], and id_in would give the same
    let sVerifyData = epochSVerifyData ci.id_out in // in fact [||], and id_in would give the same
    match role with
    | Client ->
        // FIXME: extensions should not be handled within makeClientHelloBytes!
        let cHelloBytes = makeClientHelloBytes poptions rand sid cVerifyData in
        let state = {hs_outgoing = cHelloBytes
                     hs_incoming = [||]
                     poptions = poptions
                     sDB = SessionDB.create poptions
                     pstate = PSClient (ServerHello (rand, sid, cHelloBytes))
                    }
        (ci,state)
    | Server ->
        let state = {hs_outgoing = [||]
                     hs_incoming = [||]
                     poptions = poptions
                     sDB = SessionDB.create poptions
                     pstate = PSServer (ClientHello)
                    }
        (ci,state)

let resume next_sid poptions =
    (* Resume a session, for the first time in this connection.
       Set up our state as a client. Servers cannot resume *)

    (* Search a client sid in the DB *)
    let sDB = SessionDB.create poptions in
    match select sDB next_sid with
    | None -> init Client poptions
    | Some (retrieved) ->
    let (retrievedSinfo,retrievedMS,retrievedRole) = retrieved in
    match retrievedRole with
    | Server -> init Client poptions
    | Client ->
    match retrievedSinfo.sessionID with
    | [||] -> unexpectedError "[resume_handshake] a resumed session should always have a valid sessionID"
    | sid ->
    let rand = makeRandom () in
    let ci = initConnection Client rand in
    let cVerifyData = epochCVerifyData ci.id_out in // in fact [||], and id_in would give the same
    let sVerifyData = epochSVerifyData ci.id_out in // in fact [||], and id_in would give the same
    let cHelloBytes = makeClientHelloBytes poptions rand sid cVerifyData in
    let state = {hs_outgoing = cHelloBytes
                 hs_incoming = [||]
                 poptions = poptions
                 sDB = SessionDB.create poptions
                 pstate = PSClient (ServerHello (rand, sid, cHelloBytes))
                } in
    (ci,state)

let rehandshake (ci:ConnectionInfo) (state:hs_state) (ops:config) =
    (* Start a non-resuming handshake, over an existing connection.
       Only client side, since a server can only issue a HelloRequest *)
    match state.pstate with
    | PSClient (cstate) ->
        match cstate with
        | ClientIdle ->
            let rand = makeRandom () in
            let sid = [||] in
            let cHelloBytes = makeClientHelloBytes ops rand sid (epochCVerifyData ci.id_out) in
            let state = {hs_outgoing = cHelloBytes
                         hs_incoming = [||]
                         poptions = ops
                         sDB = SessionDB.create ops
                         pstate = PSClient (ServerHello (rand, sid, cHelloBytes))
                        } in
            (true,state)
        | _ -> (* handshake already happening, ignore this request *)
            (false,state)
    | PSServer (_) -> unexpectedError "[start_rehandshake] should only be invoked on client side connections."

let rekey (ci:ConnectionInfo) (state:hs_state) (ops:config) =
    (* Start a (possibly) resuming handshake over an existing connection *)
    let si = epochSI(ci.id_out) in // or equivalently ci.id_in
    let sidOp = si.sessionID in
    match sidOp with
    | [||] -> (* Non resumable session, let's do a full handshake *)
        rehandshake ci state ops
    | sid ->
        (* Ensure the sid is in the SessionDB *)
        // FIXME: which SessionDB to use? The one in state, or create a new one from ops?
        match select state.sDB sid with
        | None -> (* Maybe session expired, or was never stored. Let's not resume *)
            rehandshake ci state ops
        | Some (retrievedSinfo,retrievedMS,retrievedDir) ->
            match retrievedDir with
            | Client ->
                match state.pstate with
                | PSClient (cstate) ->
                    match cstate with
                    | ClientIdle ->
                        let rand = makeRandom () in
                        let cHelloBytes = makeClientHelloBytes ops rand sid (epochCVerifyData ci.id_out) in
                        let state = {hs_outgoing = cHelloBytes
                                     hs_incoming = [||]
                                     poptions = ops
                                     sDB = SessionDB.create ops
                                     pstate = PSClient (ServerHello (rand, sid, cHelloBytes))
                                    } in
                        (true,state)
                    | _ -> (* Handshake already ongoing, ignore this request *)
                        (false,state)
                | PSServer (_) -> unexpectedError "[start_rekey] should only be invoked on client side connections."
            | Server ->
                (* We should not resume this session, it's for a server, we're a client *)
                rehandshake ci state ops

let request (ci:ConnectionInfo) (state:hs_state) (ops:config) =
    match state.pstate with
    | PSClient _ -> unexpectedError "[start_hs_request] should only be invoked on server side connections."
    | PSServer (sstate) ->
        match sstate with
        | ServerIdle ->
            (* Put HelloRequest in outgoing buffer (and do not log it), and move to the ClientHello state (so that we don't send HelloRequest again) *)
            (true, { hs_outgoing = makeHelloRequestBytes ()
                     hs_incoming = [||]
                     poptions = ops
                     sDB = SessionDB.create ops
                     pstate = PSServer(ClientHello)
                    })
        | _ -> (* Handshake already ongoing, ignore this request *)
            (false,state)

let invalidateSession ci state =
    let si = epochSI(ci.id_in) // FIXME: which epoch to choose? Here it matters since they could be mis-aligned
    match si.sessionID with
    | [||] -> state
    | sid ->
        let sDB = SessionDB.remove state.sDB sid in
        {state with sDB=sDB}

let getNextEpochs_resume ci si ms log crand srand =
    let sVerifyData = PRFs.makeVerifyData si Server ms log in
    let log = log @| makeMessage HT_finished sVerifyData in
    let cVerifyData = PRFs.makeVerifyData si Client ms log in
    let id_in  = nextEpoch ci.id_in  crand srand cVerifyData sVerifyData si in
    let id_out = nextEpoch ci.id_out crand srand cVerifyData sVerifyData si in
    {ci with id_in = id_in; id_out = id_out}

let getNextEpochs_full ci si ms log crand srand =
    let cVerifyData = PRFs.makeVerifyData si Client ms log in
    let log = log @| makeMessage HT_finished cVerifyData in
    let sVerifyData = PRFs.makeVerifyData si Server ms log in
    let id_in  = nextEpoch ci.id_in  crand srand cVerifyData sVerifyData si in
    let id_out = nextEpoch ci.id_out crand srand cVerifyData sVerifyData si in
    {ci with id_in = id_in; id_out = id_out}

type outgoing =
  | OutIdle of hs_state
  | OutSome of DataStream.range * Fragment.fragment * hs_state
  | OutCCS of  DataStream.range * Fragment.fragment (* the unique one-byte CCS *) *
               ConnectionInfo * StatefulAEAD.state * hs_state
  | OutFinished of DataStream.range * Fragment.fragment * hs_state
  | OutComplete of DataStream.range * Fragment.fragment * hs_state

// FIXME: cleanup when handshake is ported to streams and deltas
let makeFragment ki b =
    let stream = DataStream.init ki in
    let rg = (length b,length b) in
    let dFull = deltaPlain ki stream rg b in
    let (r0,r1) = splitRange ki rg in
    let (d,dRem) = DataStream.split ki stream r0 r1 dFull in
    let frag = deltaRepr ki stream r0 d in
    let rem = deltaRepr ki stream r1 dRem in
    let f,_ = Fragment.fragment ki stream r0 d in
    (((r0,f),(r1,dRem)),(frag,rem))

let makeCCSFragment ki b = makeFragment ki b

let next_fragment ci state =
    match state.hs_outgoing with
    | [||] ->
        match state.pstate with
        | PSClient(cstate) ->
            match cstate with
            | ClientWritingCCS (si,ms,log) ->
                let next_ci = getNextEpochs_full ci si ms log si.init_crand si.init_srand in
                let (writer,reader) = PRFs.keyGen next_ci ms in
                let cFinished = makeMessage HT_finished (epochCVerifyData next_ci.id_out) in // Equivalently, next_ci.id_in
                let state = {state with hs_outgoing = cFinished 
                                        pstate = PSClient(ServerCCS(si,ms,next_ci.id_in,reader))} in
                let (((rg,f),_),_) = makeCCSFragment ci.id_out CCSBytes in
                let ci = {ci with id_out = next_ci.id_out} in 
                OutCCS(rg,f,ci,writer,state)
            | ClientWritingCCSResume(e,w) ->
                let cFinished = makeMessage HT_finished (epochCVerifyData e) in
                let state = {state with hs_outgoing = cFinished
                                        pstate = PSClient(ClientWritingFinishedResume)} in
                let (((rg,f),_),_) = makeCCSFragment ci.id_out CCSBytes in
                let ci = {ci with id_out = e} in
                OutCCS(rg,f,ci,w,state)
            | _ -> OutIdle(state)
        | PSServer(sstate) ->
            match sstate with
            | ServerWritingCCS (si,ms,e,w) ->
                let sFinished = makeMessage HT_finished (epochSVerifyData e) in
                let state = {state with hs_outgoing = sFinished
                                        pstate = PSServer(ServerWritingFinished(si,ms))}
                let (((rg,f),_),_) = makeCCSFragment ci.id_out CCSBytes in
                let ci = {ci with id_out = e} in
                OutCCS(rg,f,ci,w,state)
            | ServerWritingCCSResume(next_ci,w,r) ->
                let sFinished = makeMessage HT_finished (epochSVerifyData next_ci.id_out) in // Equivalently, next_ci.id_in
                let state = {state with hs_outgoing = sFinished
                                        pstate = PSServer(ClientCCSResume(next_ci.id_in,r))}
                let (((rg,f),_),_) = makeCCSFragment ci.id_out CCSBytes in
                let ci = {ci with id_out = next_ci.id_out} in 
                OutCCS(rg,f,ci,w,state)
            | _ -> OutIdle(state)
    | outBuf ->
        let (((rg,f),_),(_,remBuf)) = makeFragment ci.id_out outBuf in
        let state = {state with hs_outgoing = remBuf} in
        match remBuf with
        | [||] ->
            match state.pstate with
            | PSClient(cstate) ->
                match cstate with
                | ServerCCS (_) ->
                    OutFinished(rg,f,state)
                | ClientWritingFinishedResume ->
                    let state = {state with pstate = PSClient(ClientIdle)} in
                    OutComplete(rg,f,state)
                | _ -> OutSome(rg,f,state)
            | PSServer(sstate) ->
                match sstate with
                | ServerWritingFinished(si,ms) ->
                    let sDB =
                        if equalBytes si.sessionID [||] then
                            state.sDB
                        else
                            SessionDB.insert state.sDB si.sessionID (si,ms,Server)
                    let state = {state with pstate = PSServer(ServerIdle)   
                                            sDB = sDB} in
                    OutComplete(rg,f,state)
                | ClientCCSResume(_) ->
                    OutFinished(rg,f,state)
                | _ -> OutSome(rg,f,state)
        | _ -> OutSome(rg,f,state)
                

    (* Assumptions: The buffers have been filled in the following order:
       1) hs_outgoing; 2) ccs_outgoing; 3) hs_outgoing_after_ccs
       We check 2) and 3) only if we are in a {C,S}WaitingToWrite state.
       hs_outgoing_after_ccs is filled all at once; so, when it's empty,
       we can conclude HS protocol is terminated (at least for our sending side),
       and no more data will be added to any buffer
       (until a re-handshake) *)
    // FIXME: Obsoleted by new state machine *)
    (* 
    match state.hs_outgoing with
    | [||] ->
        (* FIXME: the following code should be heavily factorized out.
           Essentially, we do the same two cases for client and server (dually), but
           we duplicate code because client and server don't share state. *)
        match state.pstate with
        | PSClient(cstate) ->
            match cstate with
            | CWaitingToWrite (cSpecState) ->
                match state.ccs_outgoing with
                | None ->
                    let (((rg,frag),dRem),(f,rem)) = makeFragment ci.id_out state.hs_outgoing_after_ccs in
                    let state = {state with hs_outgoing_after_ccs = rem} in
                    match rem with
                    | [||] ->
                        if cSpecState.resumed_session then
                            (* Handshake complete *)
                            let state = goToIdle state
                            (OutComplete (rg,frag,state))
                        else
                            (* HS write side finished *)
                            let state = {state with pstate = PSClient(CCCS(cSpecState))}
                            (OutFinished (rg,frag, state))
                    | _ -> (OutSome(rg,frag,state))
                | Some data ->
                    (* Resetting the ccs_outgoing buffer here is necessary for the current "next_fragment" logic to work.
                       It is ok to forget the associated epoch, because it has already been used to generate
                       the Finished message on our side *)
                    let state = {state with ccs_outgoing = None}
                    let (ccs,(outEpoch,writeState)) = data in
                    let ((rg,frag),dRem),(f,_) = makeCCSFragment ci.id_out ccs in
                    let ci = {ci with id_out = outEpoch} in
                    (OutCCS (rg,frag, ci, writeState, state))
            | _ -> (OutIdle(state))
        | PSServer(sstate) ->
            match sstate with
            | SWaitingToWrite (sSpecState) ->
                match state.ccs_outgoing with
                | None ->
                    let ((rg,frag),dRem),(f,rem) = makeFragment ci.id_out state.hs_outgoing_after_ccs in
                    let state = {state with hs_outgoing_after_ccs = rem} in
                    match rem with
                    | [||] ->
                        if sSpecState.resumed_session then
                            (* HS Write side finished *)
                            let state = {state with pstate = PSServer(SCCS(sSpecState))}
                            (OutFinished (rg,frag, state))
                        else
                            (* Handshake fully finished *)
                            let state = goToIdle state
                            (OutComplete (rg,frag, state))
                    | _ -> (OutSome(rg,frag, state))
                | Some data ->
                    (* Resetting the ccs_outgoing buffer here is necessary for the current "next_fragment" logic to work.
                       It is ok to forget the associated epoch, because it has already been used to generate
                       the Finished message on our side *)
                    let state = {state with ccs_outgoing = None}
                    let (ccs,(outEpoch,writeState)) = data in
                    let ((rg,frag),dRem),(f,_) = makeCCSFragment ci.id_out ccs in
                    let ci = {ci with id_out = outEpoch} in
                    (OutCCS (rg,frag, ci, writeState, state))
            | _ -> (OutIdle(state))
    | d ->
        let ((rg,frag),dRem),(f,rem) = makeFragment ci.id_out d in
        let state = {state with hs_outgoing = rem} in
        (OutSome(rg,frag,state))
*)

type incoming = (* the fragment is accepted, and... *)
  | InAck of hs_state
  | InVersionAgreed of hs_state
  | InQuery of Cert.cert * hs_state
  | InFinished of hs_state
    // FIXME: StorableSession
  | InComplete of hs_state
  | InError of ErrorCause * ErrorKind * hs_state

type incomingCCS =
  | InCCSAck of ConnectionInfo * StatefulAEAD.state * hs_state
  | InCCSError of ErrorCause * ErrorKind * hs_state

/// Certificates and Certificate Requests

let certificatesBytes certs =
    vlbytes 3 (List.foldBack (fun c a -> vlbytes 3 c @| a) certs [||])
    
let makeCertificateBytes cs =
    makeMessage HT_certificate (certificatesBytes cs)

// we need something more general for parsing lists, e.g.
let rec parseList parseOne b =
    if length b = 0 then correct([])
    else 
    match parseOne b with
    | Correct(x,b) -> 
        match parseList parseOne b with 
        | Correct(xs) -> correct(x::xs)
        | Error(x,y)  -> Error(x,y)
    | Error(x,y)      -> Error(x,y)

let rec parseCertificate_int toProcess list =
    if equalBytes toProcess [||] then
        correct(list)
    else
        match vlsplit 3 toProcess with
        | Error(x,y) -> Error(HSError(AD_bad_certificate_fatal),HSSendAlert)
        | Correct (nextCert,toProcess) ->
            let list = list @ [nextCert] in
            parseCertificate_int toProcess list

let parseCertificate data =
    match vlsplit 3 data with
    | Error(x,y) -> Error(HSError(AD_bad_certificate_fatal),HSSendAlert)
    | Correct (certList,rem) ->
    if not (equalBytes rem [||]) then
        Error(HSError(AD_bad_certificate_fatal),HSSendAlert)
    else
        match parseCertificate_int certList [] with
        | Error(x,y) -> Error(x,y)
        | Correct(certs) -> correct(certs)

let rec parseCertificateTypeList data =
    if length data = 0 then Correct([])
    else
        let (thisByte,data) = Bytes.split data 1 in
        match Cert.parseCertType thisByte with
        | Correct(ct) ->
            match parseCertificateTypeList data with
            | Correct(ctList) -> Correct(ct :: ctList)
            | Error(x,y) -> Error(x,y)
        | Error(x,y) -> Error(HSError(AD_decode_error),HSSendAlert)

let parseSigAlg b = 
    let (hashb,sigb) = Bytes.split b 1 
    match tls12enum_to_hashAlg hashb with
    | Some (hash) when checkSigAlg sigb ->
           Correct({SaHA_hash = hash; SaHA_signature = sigb })
    | _ -> Error(HSError(AD_illegal_parameter),HSSendAlert)

//CF idem, not sure re: ordering of the list and empty lists
let rec parseSigAlgs b parsed =
    match length b with 
    | 0 -> Correct(parsed)
    | 1 -> Error(HSError(AD_illegal_parameter),HSSendAlert)
    | _ -> let (b0,b) = Bytes.split b 2 
           match parseSigAlg b0 with 
           | Correct(sa) -> parseSigAlgs b (sa::parsed)
           | Error(x,y)  -> Error(x,y)  

let rec distNamesList_of_bytes data res =
    if length data = 0 then
        correct (res)
    else
        if length data < 2 then (* FIXME: maybe at least 3 bytes, because we don't want empty names... *)
            Error(Parsing,CheckFailed)
        else
            match vlsplit 2 data with
            | Error(x,y) -> Error(x,y)
            | Correct (nameBytes,data) ->
            let name = iutf8 nameBytes in (* FIXME: I have no idea wat "X501 represented in DER-encoding format" (RFC 5246, page 54) is. I assume UTF8 will do. *)
            let res = [name] @ res in
            distNamesList_of_bytes data res

let makeCertificateRequestBytes cs version =
    (* TODO: now we send all possible choices, including inconsistent ones, and we hope the client will pick the proper one. *)
    //$ make it an explicit protocol option? In the abstract protocol description we do not consider multiple choices!
    let certTypes = vlbytes 1 (Cert.certTypeBytes Cert.RSA_sign @| Cert.certTypeBytes Cert.DSA_sign @| Cert.certTypeBytes Cert.RSA_fixed_dh @| Cert.certTypeBytes Cert.RSA_fixed_dh) 
    let sigAndAlg =
        match version with
        | TLS_1p2 ->
            (* For no particular reason, we will offer rsa-sha1 and dsa-sha1 *)
            let sha1B   = hashAlg_to_tls12enum Algorithms.hashAlg.SHA in
            let sigAndAlg = sha1B @| SA_rsa @| sha1B @| SA_dsa in
            vlbytes 2 sigAndAlg
        | _ -> [||]
    (* We specify no cert auth *)
    let distNames = vlbytes 2 [||] in
    let data = certTypes 
            @| sigAndAlg 
            @| distNames in
    makeMessage HT_certificate_request data

let parseCertificateRequest version data =
    match vlsplit 1 data with
    | Error(x,y) -> Error(HSError(AD_illegal_parameter),HSSendAlert)
    | Correct (certTypeListBytes,data) ->
    match parseCertificateTypeList certTypeListBytes with
    | Error(x,y) -> Error(x,y)
    | Correct(certTypeList) ->
    let sigAlgsAndData = (
        if version = TLS_1p2 then
            match vlsplit 2 data with
            | Error(x,y) -> Error(HSError(AD_illegal_parameter),HSSendAlert)
            | Correct (sigAlgsBytes,data) ->
            match parseSigAlgs sigAlgsBytes [] with
            | Error(x,y) -> Error(x,y)               
            | Correct (sigAlgsList) -> correct (Some(sigAlgsList),data)
        else
            correct (None,data)) in
    match sigAlgsAndData with
    | Error(x,y) -> Error(x,y)
    | Correct ((sigAlgs,data)) ->
    match vlsplit 2 data with
    | Error(x,y) -> Error(HSError(AD_illegal_parameter),HSSendAlert)
    | Correct  (distNamesBytes,_) ->
    match distNamesList_of_bytes distNamesBytes [] with
    | Error(x,y) -> Error(HSError(AD_illegal_parameter),HSSendAlert)
    | Correct distNamesList ->
    let res = { client_certificate_type = certTypeList;
                signature_and_hash_algorithm = sigAlgs;
                certificate_authorities = distNamesList} in
    correct (res)

/// ServerHelloDone

let serverHelloDoneBytes = makeMessage HT_server_hello_done [||] 


/// ClientKeyExchange

let makeClientKEX_RSA si config =
    let pms = RSAPlain.genPMS si config.maxVer in
    if si.serverID.IsEmpty then
        unexpectedError "[makeClientKEX_RSA] Server certificate should always be present with a RSA signing cipher suite."
    else
        let pubKey = pubKey_of_certificate si.serverID.Head in
        let encpms = RSAEnc.encrypt pubKey si pms in
        let encpms = if si.protocol_version = SSL_3p0 then encpms else vlbytes 2 encpms 
        ((makeMessage HT_client_key_exchange encpms),pms)

let makeClientKEX_DH_explicit si config =
    // TODO
    makeMessage HT_client_key_exchange [||]

let makeClientKEX_DH_implicit = makeMessage HT_client_key_exchange [||]

let parseClientKEX_RSA si cv config data =
    if si.serverID.IsEmpty then
        unexpectedError "[parseClientKEX] when the ciphersuite can encrypt the PMS, the server certificate should always be set"
    else
        let encrypted = (* parse the message *)
            match si.protocol_version with
            | SSL_3p0 -> correct (data)
            | TLS_1p0 | TLS_1p1| TLS_1p2 ->
                    match vlparse 2 data with
                    | Correct (encPMS) -> correct(encPMS)
                    | Error(x,y) -> Error(HSError(AD_decode_error),HSSendAlert)
        match encrypted with
        | Correct(encPMS) ->
            let res = RSAEnc.decrypt_PMS (prikey_of_cert si.serverID.Head) si cv config.check_client_version_in_pms_for_old_tls encPMS in
            correct(res)
        | Error(x,y) -> Error(x,y)

let parseClientKEX_DH_implict data =
    if length data = 0 then
        correct ( () )
    else
        Error(HSError(AD_decode_error),HSSendAlert)

let parseClientKEX_DH_explicit data = correct( () ) // TODO

let makeCertificateVerifyBytes (cert: Cert.cert list) data pv =
    if cert.IsEmpty then
        correct (makeMessage HT_certificate_verify [||])
    else
    let cert = cert.Head in
    match pv with
    | TLS_1p2 ->
        (* If DSA, use SHA-1 hash *)
        if certificate_is_dsa cert then (* TODO *)
            (*let hash = sha1 data in
            let signed = dsa_sign priKey hash in *)
            correct ([||])
        else
            (* Get server preferred hash algorithm *)
            let hashAlg =
                match certReqMsg.signature_and_hash_algorithm with
                | Some (sahaList) -> sahaList.Head.SaHA_hash
                | None -> unexpectedError "[makeCertificateVerifyBytes] We are in TLS 1.2, so the server should send a SigAndHashAlg structure."
            let hashed = HASH.hash hashAlg data in
            let priKey = priKey_of_certificate cert in
            // THIS IS NOT AN ENCRYPTION!
            // we should pick the signing alg from cert. Not rsaEncrypt!!
            match RSA.encrypt priKey hashed with
            | Error (x,y) -> Error(HSError(AD_decrypt_error),HSSendAlert)
            | Correct (signed) ->
                let signed = vlbytes 2 signed in
                let hashAlgBytes = hashAlg_to_tls12enum hashAlg in
                let payload = hashAlgBytes @| SA_rsa @| signed in
                correct (makeMessage HT_certificate_verify payload)
    | TLS_1p0 | TLS_1p1 ->
        (* TODO *) Error(HSError(AD_internal_error),HSSendAlert)
    | SSL_3p0 ->
        (* TODO *) Error(HSError(AD_internal_error),HSSendAlert)
    
//$ todo?
let find_client_cert (certReqMsg:certificateRequest) : (Cert.cert list) =
    (* TODO *) []


let certificateVerifyCheck (state:hs_state) (payload:bytes) =
    (* TODO: pretend client sent valid verification data. 
       We need to understand how to treat certificates and related algorithms properly *)
    correct(true)

let prepare_client_output_full_RSA (ci:ConnectionInfo) state (si:SessionInfo) log =
    let clientCertBytes =
      if si.certificate_request then
        makeCertificateBytes si.clientID
      else [||]

    let log = log @| clientCertBytes in

    let (clientKEXBytes,pms) = makeClientKEX_RSA si state.poptions in

    let log = log @| clientKEXBytes in

    let ms = PRFs.prfSmoothRSA si pms in
    (* FIXME: here we should shred pms *)
    let certificateVerifyBytesResult =
        if si.certificate_request then
            makeCertificateVerifyBytes si.clientID log si.protocol_version
        else
            (* No client certificate ==> no certificateVerify message *)
            correct ([||])
    match certificateVerifyBytesResult with
    | Error (x,y) -> Error (x,y)
    | Correct (certificateVerifyBytes) ->
        let log = log @| certificateVerifyBytes in

        (* Enqueue current messages in output buffer *)
        let to_send = clientCertBytes @| clientKEXBytes @| certificateVerifyBytes in
        let new_outgoing = state.hs_outgoing @| to_send in
        let state = {state with hs_outgoing = new_outgoing} in
        correct (state,ms,log)
 
let on_serverHello_full crand log shello =
    let si = { clientID = []
               serverID = []
               certificate_request = false
               sessionID = shello.sh_session_id
               protocol_version = shello.sh_server_version
               cipher_suite = shello.sh_cipher_suite
               compression = shello.sh_compression_method
               init_crand = crand
               init_srand = shello.sh_random
               } in
    (* If DH_ANON, go into the ServerKeyExchange state, else go to the Certificate state *)
    if isAnonCipherSuite shello.sh_cipher_suite then
        PSClient(ServerKeyExchangeDH_anon(si,log))
    elif isDHCipherSuite shello.sh_cipher_suite then
        PSClient(ServerCertificateDH(si,log))
    elif isDHECipherSuite shello.sh_cipher_suite then
        PSClient(ServerCertificateDHE(si,log))
    elif isRSACipherSuite shello.sh_cipher_suite then
        PSClient(ServerCertificateRSA(si,log))
    else
        unexpectedError "[recv_fragment] Unknown ciphersuite"

let rec recv_fragment_client (ci:ConnectionInfo) (state:hs_state) (agreedVersion:ProtocolVersion option) =
    match parseMessage state with
    | None ->
      match agreedVersion with
      | None      -> InAck(state)
      | Some (pv) -> InVersionAgreed(state)
    | Some (state,hstype,payload,to_log) ->
      match state.pstate with
      | PSClient(cState) ->
        match hstype with
        | HT_hello_request ->
            match cState with
            | ClientIdle -> (* This is a legitimate hello request. Properly handle it *)
                (* Do not log this message *)
                match state.poptions.honourHelloReq with
                | HRPIgnore -> recv_fragment_client ci state agreedVersion
                | HRPResume -> let (_,state) = rekey ci state state.poptions in InAck(state) (* Terminating case, we're not idle anymore *)
                | HRPFull   -> let (_,state) = rehandshake ci state state.poptions in InAck(state) (* Terminating case, we're not idle anymore *)
            | _ -> (* RFC 7.4.1.1: ignore this message *) recv_fragment_client ci state agreedVersion
        | HT_server_hello ->
            match cState with
            | ServerHello (crand,sid,log) ->
                match parseServerHello payload with
                | Error(x,y) -> InError(HSError(AD_decode_error),HSSendAlert,state)
                | Correct (shello) ->
                  // Sanity checks on the received message; they are security relevant. 
                  // Check that the server agreed version is between maxVer and minVer.
                  if not (geqPV shello.sh_server_version state.poptions.minVer 
                       && geqPV state.poptions.maxVer shello.sh_server_version) 
                  then InError(HSError(AD_protocol_version),HSSendAlert,state)
                  else
                  // Check that the negotiated ciphersuite is in the proposed list.
                  // Note: if resuming a session, we still have to check that this ciphersuite is the expected one!
                  if not (List.exists (fun x -> x = shello.sh_cipher_suite) state.poptions.ciphersuites) 
                  then InError(HSError(AD_illegal_parameter),HSSendAlert,state)
                  else
                  // Check that the compression method is in the proposed list.
                  if not (List.exists (fun x -> x = shello.sh_compression_method) state.poptions.compressions) 
                  then InError(HSError(AD_illegal_parameter),HSSendAlert,state)
                  else
                  // Handling of safe renegotiation
                  let safe_reneg_result =
                    if state.poptions.safe_renegotiation then
                        let expected = (epochCVerifyData ci.id_in) @| (epochSVerifyData ci.id_in) in // or, equivalenty, ci.id_out
                        inspect_ServerHello_extensions shello.sh_neg_extensions expected
                    else
                        // RFC Sec 7.4.1.4: with no safe renegotiation, we never send extensions; if the server sent any extension
                        // we MUST abort the handshake with unsupported_extension fatal alter (handled by the dispatcher)
                        if not (equalBytes shello.sh_neg_extensions [||])
                        then Error(HSError(AD_unsupported_extension),HSSendAlert)
                        else let unitVal = () in correct (unitVal)
                  match safe_reneg_result with
                    | Error (x,y) -> InError (x,y,state)
                    | Correct _ ->
                        // Log the received packet.
                        let log = log @| to_log in
                        (* Check whether we asked for resumption *)
                        if equalBytes sid [||] then
                            (* we did not request resumption, do a full handshake *)
                            (* define the sinfo we're going to establish *)
                            let next_pstate = on_serverHello_full crand log shello in
                            let state = {state with pstate = next_pstate} in
                            recv_fragment_client ci state (Some(shello.sh_server_version))
                        else
                            if equalBytes sid shello.sh_session_id then (* use resumption *)
                                (* Search for the session in our DB *)
                                match SessionDB.select state.sDB sid with
                                | None ->
                                    (* This can happen, although we checked for the session before starting the HS.
                                       For example, the session may have expired between us sending client hello, and now. *)
                                    InError(HSError(AD_internal_error),HSSendAlert,state)
                                | Some(storable) ->
                                let (si,ms,role) = storable in
                                (* Check that protocol version, ciphersuite and compression method are indeed the correct ones *)
                                if si.protocol_version = shello.sh_server_version then
                                    if si.cipher_suite = shello.sh_cipher_suite then
                                        if si.compression = shello.sh_compression_method then
                                            let next_ci = getNextEpochs_resume ci si ms log crand shello.sh_random in
                                            let (writer,reader) = PRFs.keyGen next_ci ms in
                                            let state = {state with pstate = PSClient(ServerCCSResume(next_ci,writer,reader))} in
                                            recv_fragment_client ci state (Some(shello.sh_server_version))
                                        else InError(HSError(AD_illegal_parameter),HSSendAlert,state)
                                    else InError(HSError(AD_illegal_parameter),HSSendAlert,state)
                                else InError(HSError(AD_illegal_parameter),HSSendAlert,state)
                            else (* server did not agree on resumption, do a full handshake *)
                                (* define the sinfo we're going to establish *)
                                let next_pstate = on_serverHello_full crand log shello in
                                let state = {state with pstate = next_pstate} in
                                recv_fragment_client ci state (Some(shello.sh_server_version))
            | _ -> (* ServerHello arrived in the wrong state *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
        | HT_certificate ->
            match cState with
            // FIXME: Most of the code in the branches is duplicated
            | ServerCertificateRSA (si,log) ->
                match parseCertificate payload with
                | Error(x,y) -> InError(x,y,state)
                | Correct(certs) ->
                    if Cert.is_for_key_encryption certs || false then // FIXME: " || Cert.verify certs " ; FIXME: we still have to ask the user
                        InError(HSError(AD_bad_certificate_fatal),HSSendAlert,state)
                    else (* We have validated server identity *)
                        (* Log the received packet *)
                        let log = log @| to_log in        
                        (* update the sinfo we're establishing *)
                        let si = {si with serverID = certs} in
                        let state = {state with pstate = PSClient(CertificateRequestRSA(si,log))} in
                        recv_fragment_client ci state agreedVersion
            | ServerCertificateDHE (si,log) ->
                match parseCertificate payload with
                | Error(x,y) -> InError(x,y,state)
                | Correct(certs) ->
                    if Cert.is_for_signing certs || false then // FIXME: " || Cert.verify certs " ; FIXME: we still have to ask the user
                        InError(HSError(AD_bad_certificate_fatal),HSSendAlert,state)
                    else (* We have validated server identity *)
                        (* Log the received packet *)
                        let log = log @| to_log in        
                        (* update the sinfo we're establishing *)
                        let si = {si with serverID = certs} in
                        let state = {state with pstate = PSClient(CertificateRequestDHE(si,log))} in
                        recv_fragment_client ci state agreedVersion
            | ServerCertificateDH (si,log) -> InError(HSError(AD_internal_error),HSSendAlert,state) // TODO
            | _ -> (* Certificate arrived in the wrong state *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
        | HT_server_key_exchange ->
            match cState with
            | ServerKeyExchangeDHE(si,log) ->
                (* TODO *) InError(HSError(AD_internal_error),HSSendAlert,state)
            | ServerKeyExchangeDH_anon(si,log) -> (* TODO *) InError(HSError(AD_internal_error),HSSendAlert,state)
            | _ -> (* Server Key Exchange arrived in the wrong state *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
        | HT_certificate_request ->
            match cState with
            | CertificateRequestRSA(si,log) ->
                (* Log the received packet *)
                let log = log @| to_log in

                (* Note: in next statement, use si, because the handshake runs according to the session we want to
                   establish, not the current one *)
                match parseCertificateRequest si.protocol_version payload with
                | Error(x,y) -> InError(x,y,state)
                | Correct(certReqMsg) ->
                let client_cert = find_client_cert certReqMsg in
                (* Update the sinfo we're establishing *)
                let si = {si with clientID = client_cert; certificate_request = true} in
                let state = {state with pstate = PSClient(ServerHelloDoneRSA(si,log))} in
                recv_fragment_client ci state agreedVersion
            | _ -> (* Certificate Request arrived in the wrong state *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
        | HT_server_hello_done ->
            match cState with
            | CertificateRequestRSA(si,log) | ServerHelloDoneRSA(si,log) ->
                if not (equalBytes payload [||]) then
                    InError(HSError(AD_decode_error),HSSendAlert,state)
                else
                    (* Log the received packet *)
                    let log = log @| to_log in

                    match prepare_client_output_full_RSA ci state si log with
                    | Error (x,y) -> InError (x,y, state)
                    | Correct (state,ms,log) ->
                        let state = {state with pstate = PSClient(ClientWritingCCS(si,ms,log))}
                        recv_fragment_client ci state agreedVersion
            | _ -> (* Server Hello Done arrived in the wrong state *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
        | HT_finished ->
            match cState with
            | ServerFinished(si,ms) ->
                if not (equalBytes payload (epochSVerifyData ci.id_in)) then
                    InError(HSError(AD_decrypt_error),HSSendAlert,state)
                else
                    let sDB =
                        if equalBytes si.sessionID [||] then
                            state.sDB
                        else
                            SessionDB.insert state.sDB si.sessionID (si,ms,Client)
                    let state = {state with pstate = PSClient(ClientIdle)
                                            sDB = sDB} in
                    InComplete(state)
                    

            | ServerFinishedResume(e,w) ->
                if not (equalBytes payload (epochSVerifyData ci.id_in)) then
                    InError(HSError(AD_decrypt_error),HSSendAlert,state)
                else
                    let state = {state with pstate = PSClient(ClientWritingCCSResume(e,w))} in
                    InFinished(state)
            | _ -> (* Finished arrived in the wrong state *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
        | _ -> (* Unsupported/Wrong message *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
      
      (* Should never happen *)
      | PSServer(_) -> unexpectedError "[recv_fragment_client] should only be invoked when in client role."

let prepare_server_output_full state maxClVer =
    let ext = 
      if state.poptions.safe_renegotiation then
        let data = state.hs_renegotiation_info_cVerifyData @| state.hs_renegotiation_info_sVerifyData in
        let ren_extB = makeRenegExtBytes data in
        makeExtBytes ren_extB
      else
        [||]
    let serverHelloB = makeServerHelloBytes state.hs_next_info state.ki_srand ext  in
    let next_info = {state.hs_next_info with init_srand = state.ki_srand} in
    let state = {state with hs_next_info = next_info} in
    let res =
        if isAnonCipherSuite state.hs_next_info.cipher_suite then
            correct ([||],state)
        else
            match getServerCert state.hs_next_info.cipher_suite state.poptions with
            | Error(x,y) -> Error(x,y)
            | Correct(sCert) ->
                (* update server identity in the sinfo *)
                let next_info = {state.hs_next_info with serverID = Some(sCert)} in
                let state = {state with hs_next_info = next_info} in
                correct (makeCertificateBytes (Some([sCert])), state)
    match res with
    | Error(x,y) -> Error(x,y)
    | Correct (res) ->
        let (certificateB,state) = res in
        let res =
            if isAnonCipherSuite state.hs_next_info.cipher_suite || cipherSuiteRequiresKeyExchange state.hs_next_info.cipher_suite then
                (* TODO: DH key exchange *)
                Error(HSError(AD_internal_error),HSSendAlert)
            else
                correct ([||])
        match res with
        | Error(x,y) -> Error(x,y)
        | Correct (serverKeyExchangeB) ->
            let certificateRequestB =
                if state.poptions.request_client_certificate then
                    makeCertificateRequestBytes state.hs_next_info.cipher_suite state.hs_next_info.protocol_version
                else
                    [||]
            let output = serverHelloB @| certificateB @| serverKeyExchangeB @| certificateRequestB @| serverHelloDoneBytes in
            (* Log the output and put it into the output buffer *)
            let new_log = state.hs_msg_log @| output in
            let new_out = state.hs_outgoing @| output in
            let state = {state with hs_msg_log = new_log; hs_outgoing = new_out} in
            (* Compute the next state of the server *)
            let sSpecSt = { resumed_session = false
                            highest_client_ver = maxClVer} in
            let state =
                if state.poptions.request_client_certificate then
                    {state with pstate = PSServer(ClCert(sSpecSt))}
                else
                    {state with pstate = PSServer(ClientKEX(sSpecSt))}
            correct (state)


// The server "negotiates" its first proposal included in the client's proposal
let negotiate cList sList =
    List.tryFind (fun s -> List.exists (fun c -> c = s) cList) sList

let prepare_server_output_resumption ci state =
    let ext = 
      if state.poptions.safe_renegotiation then
        let data = state.hs_renegotiation_info_cVerifyData @| state.hs_renegotiation_info_sVerifyData in
        let ren_extB = makeRenegExtBytes data in
        makeExtBytes ren_extB
      else
        [||]
    let sHelloB = makeServerHelloBytes state.hs_next_info state.ki_srand ext in
    let new_out = state.hs_outgoing @| sHelloB in
    let new_log = state.hs_msg_log  @| sHelloB in
    let state = {state with hs_outgoing = new_out; hs_msg_log = new_log} in
    let state = compute_session_secrets_and_CCSs ci state in
    let ki =
        match state.ccs_outgoing with
        | None -> unexpectedError "[prepare_server_output_resumption] The ccs_outgoing buffer should contain some value when computing the finished message"
        | Some (_,(ki,ccs_data)) -> ki
    let si = epochSI(ki) in
    let (finishedB,verifyData) = makeFinishedMsgBytes si Server state.next_ms state.hs_msg_log in
    (* match makeFinishedMsgBytes sinfo.protocol_version sinfo.cipher_suite sinfo.more_info.mi_ms Server state.hs_msg_log with *)
    let new_out = state.hs_outgoing_after_ccs @| finishedB in
    let new_log = state.hs_msg_log @| finishedB in
    let sSpecState = {resumed_session = true
                      highest_client_ver = state.hs_next_info.protocol_version} (* Highest version is useless with resumption. We already agree on the MS *)
    let state = {state with hs_outgoing_after_ccs = new_out
                            hs_msg_log = new_log
                            hs_renegotiation_info_sVerifyData = verifyData
                            pstate = PSServer(SWaitingToWrite(sSpecState))} in
    state

let rec recv_fragment_server (ci:ConnectionInfo) (state:hs_state) (agreedVersion:ProtocolVersion option) =
    match parseMessage state with
    | None ->
      match agreedVersion with
      | None      -> InAck(state)
      | Some (pv) -> InVersionAgreed(state)
    | Some (state,hstype,payload,to_log) ->
      match state.pstate with
      | PSServer(sState) ->
        match hstype with
        | HT_client_hello ->
            match sState with
            | x when x = ClientHello || x = ServerIdle ->
                match parseClientHello payload with
                | Error(x,y) -> InError(HSError(AD_decode_error),HSSendAlert,state)
                | Correct (cHello) ->
                (* Log the received message *)
                let log = to_log in
                (* handle extensions: for now only renegotiation_info *)
                let extRes =
                    if state.poptions.safe_renegotiation then
                        if check_client_renegotiation_info cHello state.hs_renegotiation_info_cVerifyData then
                            correct(state)
                        else
                            (* We don't accept an insecure client *)
                            Error(HSError(AD_handshake_failure),HSSendAlert)
                    else
                        (* We can ignore the extension, if any *)
                        correct(state)
                match extRes with
                | Error(x,y) -> InError(x,y,state)
                | Correct(state) ->
                    (* Check whether the client asked for session resumption *)
                    if equalBytes cHello.ch_session_id [||] 
                    then 
                        (* Client asked for a full handshake *)
                        startServerFull ci state cHello
                    else
                        (* Client asked for resumption, let's see if we can satisfy the request *)
                        match select state.sDB cHello.ch_session_id with
                        | None ->
                            (* We don't have the requested session stored, go for a full handshake *)
                            startServerFull ci state cHello
                        | Some (storedSinfo,storedMS,storedDir) ->
                            (* Check that the client proposed algorithms match those of our stored session *)
                            match storedDir with
                            | Client -> (* This session is not for us, we're a server. Do full handshake *)
                                startServerFull ci state cHello
                            | Server ->
                                if cHello.ch_client_version >= storedSinfo.protocol_version then
                                    (* We have a common version *)
                                    if not (List.exists (fun cs -> cs = storedSinfo.cipher_suite) cHello.ch_cipher_suites) then
                                        (* Do a full handshake *)
                                        startServerFull ci state cHello
                                    else if not (List.exists (fun cm -> cm = storedSinfo.compression) cHello.ch_compression_methods) then
                                        (* Do a full handshake *)
                                        startServerFull ci state cHello
                                    else
                                        (* Everything is ok, proceed with resumption *)
                                        let state = {state with hs_next_info = storedSinfo
                                                                next_ms = storedMS}
                                        let state = prepare_server_output_resumption ci state 
                                        recv_fragment_server ci state (Some(storedSinfo.protocol_version))
                                else
                                    (* Do a full handshake *)
                                    startServerFull ci state cHello
                                    
            | _ -> (* Message arrived in the wrong state *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
        | HT_certificate ->
            match sState with
            | ClCert (sSpecSt) ->
                match parseCertificate payload with
                | Error(x,y) -> InError(x,y,state)
                | Correct(certs) ->
                    if false then // FIXME: not (certs.certificate_list trusted by state.poptions.trustedRootCertificates)
                        InError(HSError(AD_bad_certificate_fatal),HSSendAlert,state)
                    else (* We have validated client identity *)
                        (* Log the received packet *)
                        let new_log = state.hs_msg_log @| to_log in
                        let state = {state with hs_msg_log = new_log} in           
                        (* update the sinfo we're establishing *)
                        let next_info =
                            match certs with 
                            | []   -> {state.hs_next_info with clientID = None}
                            | c::_ -> {state.hs_next_info with clientID = Some(c) }
                        let state = {state with hs_next_info = next_info} in
                        (* move to the next state *)
                        let state = {state with pstate = PSServer(ClientKEX(sSpecSt))} in
                        recv_fragment_server ci state agreedVersion
            | _ -> (* Message arrived in the wrong state *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
        | HT_client_key_exchange ->
            match sState with
            | ClientKEX(sSpecSt) ->
                match parseClientKEX state.hs_next_info sSpecSt state.poptions payload with
                | Error(x,y) -> InError(x,y,state)
                | Correct(pms) ->
                    (* Log the received packet *)
                    let new_log = state.hs_msg_log @| to_log in
                    let state = {state with hs_msg_log = new_log} in
                    let ms = prfMS state.hs_next_info pms in
                    (* assert: state.hs_next_info.{c,s}rand = state.ki_{c,s}rand *)
                    (* match compute_master_secret pms sinfo.more_info.mi_protocol_version state.hs_client_random state.hs_server_random with *)
                    (* TODO: here we should shred pms *)
                    let state = {state with next_ms = ms} in
                    let state = compute_session_secrets_and_CCSs ci state in
                        (* move to new state *)
                    match state.hs_next_info.clientID with
                    | None -> (* No client certificate, so there will be no CertificateVerify message *)
                        let state = {state with pstate = PSServer(SCCS(sSpecSt))} in
                        recv_fragment_server ci state agreedVersion
                    | Some(cert) ->
                        if certificate_has_signing_capability cert then
                            let state = {state with pstate = PSServer(CertificateVerify(sSpecSt))} in
                            recv_fragment_server ci state agreedVersion
                        else
                            let state = {state with pstate = PSServer(SCCS(sSpecSt))} in
                            recv_fragment_server ci state agreedVersion
            | _ -> (* Message arrived in the wrong state *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
        | HT_certificate_verify ->
            match sState with
            | CertificateVerify(sSpecSt) ->
                match state.hs_next_info.clientID with
                | None -> (* There should always be a client certificate in this state *)InError(HSError(AD_internal_error),HSSendAlert,state)
                | Some(clCert) ->
                    match certificateVerifyCheck state payload with
                    | Error(x,y) -> InError(x,y,state)
                    | Correct(verifyOK) ->
                        if verifyOK then
                            (* Log the message *)
                            let new_log = state.hs_msg_log @| to_log in
                            let state = {state with hs_msg_log = new_log} in   
                            (* move to next state *)
                            let state = {state with pstate = PSServer(SCCS(sSpecSt))} in
                            recv_fragment_server ci state agreedVersion
                        else
                            InError(HSError(AD_decrypt_error),HSSendAlert,state)
            | _ -> (* Message arrived in the wrong state *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
        | HT_finished ->
            match sState with
            | SFinished(sSpecSt) ->
                (* Obsolete: The current ki should be the right one *)
                (*
                let ki =
                    match state.ccs_incoming with
                    | None -> unexpectedError "[recv_fragment_server] the incoming epoch should be set now"
                    | Some (ki,ccs_data) -> ki
                *)
                let siIn = epochSI(ci.id_in) in
                let verifyDataisOK = checkVerifyData siIn Client state.next_ms state.hs_msg_log payload in
                (* match checkVerifyData sinfo.protocol_version sinfo.cipher_suite sinfo.more_info.mi_ms Client state.hs_msg_log payload with *)
                if not verifyDataisOK then
                    InError(HSError(AD_decrypt_error),HSSendAlert,state)
                else
                    (* Save client verify data to possibly use it in the renegotiation_info extension *)
                    let state = {state with hs_renegotiation_info_cVerifyData = payload} in
                    if sSpecSt.resumed_session then
                        (* Handshake fully completed successfully. Report this fact to the dispatcher. *)
                        (* Note: no need to log this message (and we go to the idle state forgetting everything anyway) *)
                        let state = goToIdle state
                        InComplete(state)
                    else
                        (* Log the received message *)
                        let new_log = state.hs_msg_log @| to_log in
                        let state = {state with hs_msg_log = new_log} in
                        let kiOut =
                            match state.ccs_outgoing with
                            | None -> unexpectedError "[recv_fragment_server] Outgoing epoch should be set now"
                            | Some(_,(kiOut,ccs_data)) -> kiOut
                        let siOut = epochSI(kiOut) in
                        let (packet,verifyData) = makeFinishedMsgBytes siOut Server state.next_ms state.hs_msg_log in
                        (* match makeFinishedMsgBytes sinfo.protocol_version sinfo.cipher_suite sinfo.more_info.mi_ms Server state.hs_msg_log with *)
                        let new_out = state.hs_outgoing_after_ccs @| packet in
                        let state = {state with hs_outgoing_after_ccs = new_out
                                                hs_renegotiation_info_sVerifyData = verifyData
                                                pstate = PSServer(SWaitingToWrite(sSpecSt))} in
                        InFinished(state)                                
            | _ -> (* Message arrived in the wrong state *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
        | _ -> (* Unsupported/Wrong message *) InError(HSError(AD_unexpected_message),HSSendAlert,state)
      (* Should never happen *)
      | PSClient(_) -> unexpectedError "[recv_fragment_server] should only be invoked when in server role."
      
and startServerFull (ci:ConnectionInfo) state cHello =  
    // Negotiate the protocol parameters
    let version = minPV cHello.ch_client_version state.poptions.maxVer in
    if not (geqPV version state.poptions.minVer) then
        InError(HSError(AD_handshake_failure),HSSendAlert,state)
    else
        match negotiate cHello.ch_cipher_suites state.poptions.ciphersuites with
        | Some(cs) ->
            match negotiate cHello.ch_compression_methods state.poptions.compressions with
            | Some(cm) ->
                (* TODO: now we don't support safe_renegotiation, and we ignore any client proposed extension *)
                let sid = mkRandom 32 in
                (* Fill in the session info we're establishing *)
                let next_info = { clientID         = None
                                  serverID         = None
                                  sessionID        = Some(sid)
                                  protocol_version = version
                                  cipher_suite     = cs
                                  compression      = cm
                                  init_crand       = state.ki_crand
                                  init_srand       = [||] }
                let state = {state with hs_next_info = next_info} in
                match prepare_server_output_full state cHello.ch_client_version with
                | Correct(state) -> recv_fragment_server ci state (Some(version)) 
                | Error(x,y)     -> InError(x,y,state)
            | None -> InError(HSError(AD_handshake_failure),HSSendAlert,state)
        | None ->     InError(HSError(AD_handshake_failure),HSSendAlert,state)


let enqueue_fragment (ci:ConnectionInfo) state fragment =
    let new_inc = state.hs_incoming @| fragment in
    {state with hs_incoming = new_inc}

let recv_fragment ci (state:hs_state) (r:DataStream.range) (fragment:Fragment.fragment) =
    // FIXME: cleanup when Hs is ported to streams and deltas
    let b = Fragment.fragmentRepr ci.id_in r fragment in 
    if length b = 0 then
        // Empty HS fragment are not allowed
        InError(HSError(AD_decode_error),HSSendAlert,state)
    else
        let state = enqueue_fragment ci state b in
        match state.pstate with
        | PSClient (_) -> recv_fragment_client ci state None
        | PSServer (_) -> recv_fragment_server ci state None

let recv_ccs (ci:ConnectionInfo) (state: hs_state) (r:DataStream.range) (fragment:Fragment.fragment): incomingCCS =
    // FIXME: cleanup when Hs is ported to streams and deltas
    let b = Fragment.fragmentRepr ci.id_in r fragment in 
    if equalBytes b CCSBytes then  
        match state.pstate with
        | PSClient (cstate) -> // Check that we are in the right state (CCCS) 
            match cstate with
            | ServerCCS(si,ms,e,r) ->
                let state = {state with pstate = PSClient(ServerFinished(si,ms))} in
                let ci = {ci with id_in = e} in
                InCCSAck(ci,r,state)
            | ServerCCSResume(next_ci,w,r) ->
                let state = {state with pstate = PSClient(ServerFinishedResume(next_ci.id_out,w))} in
                let ci = {ci with id_in = next_ci.id_in} in
                InCCSAck(ci,r,state)
            | _ -> InCCSError(HSError(AD_unexpected_message),HSSendAlert,state)
        | PSServer (sState) ->
            match sState with
            | SCCS (sSpecSt) ->
                match state.ccs_incoming with
                | Some (ccs_result) ->
                    let state = {state with (* ccs_incoming = None *) (* Don't reset its value now. We'll need it when computing the other side Finished message *)
                                            pstate = PSServer(SFinished(sSpecSt))}
                    let (inEpoch,rState) = ccs_result in
                    let ci = {ci with id_in = inEpoch} in
                    InCCSAck(ci,rState,state)
                | None -> unexpectedError "[recv_ccs] when in CCCS state, ccs_incoming should have some value."
            | _ -> InCCSError(HSError(AD_unexpected_message),HSSendAlert,state)
    else           InCCSError(HSError(AD_decode_error)      ,HSSendAlert,state)

let getNegotiatedVersion (ci:ConnectionInfo) state = state.hs_next_info.protocol_version
let getMinVersion (ci:ConnectionInfo) state = state.poptions.minVer

let authorize (ci:ConnectionInfo) (s:hs_state) (q:Cert.cert) = s // TODO

(* function used by an ideal handshake implementation to decide whether to idealize keys
let safe ki = 
    match (CS(ki), Honest(LTKey(ki, Server)), Honest(LTKey(ki,Client))) with
    | (CipherSuite (RSA, MtE (AES_256_CBC, SHA256)), true, _) -> pmsGenerated ki            
    | (CipherSuite (DHE_DSS, MtE (AES_256_CBC, SHA)), _, _) -> 
        if (TcGenerated ki) && (TsGenerated ki) then 
            true 
        else 
            false
    | _ -> false

 *)
