﻿(* Handshake protocol *) 
module Handshake

open Data
open Bytearray
open Record
open Error_handling
open Formats
open HS_msg
open HS_ciphersuites
open Sessions
open Crypto

(* We may be sending either HS messages, or a CCS message, 
   but not both at the same time (really?) *)

type output_state = 
  | SendCCS
  | SendHS of bytes 

type clientState =
    | ServerHello
    | Certificate
    | ServerKeyExchange
    | CertReq
    | CCCS
    | CFinished
    | CIdle

type serverState =
    | ClientHello
    | Keying
    | ClientKEX
    | CertificateVerify
    | SCCS
    | SFinished
    | SIdle

type protoState =
    | Client of clientState
    | Server of serverState

type protocolOptions = {
    minVer: ProtocolVersionType
    maxVer: ProtocolVersionType
    ciphersuites: cipherSuites
    compressions: Compression list
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
    }

type hs_state = {
  hs_outgoing    : bytes (* outgiong data before a ccs *)
  ccs_outgoing: (bytes * ccs_data) option (* marker telling there's a ccs ready *)
  hs_outgoing_after_ccs: bytes (* data to be sent after the ccs has been sent *)
  hs_incoming    : bytes (* partial incoming HS message *)
  hs_info : SessionInfo;
  poptions: protocolOptions
  pstate : protoState
}

type HSFragReply =
  | EmptyHSFrag
  | HSFrag of bytes
  | LastHSFrag of bytes (* Useful to let the dispatcher switch to the Open state *)
  | CCSFrag of bytes * ccs_data

let next_fragment state len =
    (* FIXME: The buffer we read from should depend on the state of the protocol,
       and not on whether a buffer is full or not, otherwise we cannot recognize the
       LastHSFrag() case! *)
    match state.hs_outgoing with
    | x when equalBytes x empty_bstr ->
        match state.ccs_outgoing with
        | None -> (EmptyHSFrag, state)
        | Some x ->
            let (ccs,ciphsuite) = x in
            let new_hs_outgoing = state.hs_outgoing_after_ccs in
            let state = {state with hs_outgoing = new_hs_outgoing;
                                    ccs_outgoing = None;
                                    hs_outgoing_after_ccs = empty_bstr}
            (CCSFrag (ccs,ciphsuite), state)
    | d ->
        let (f,rem) = split d len in
        let state = {state with hs_outgoing = rem} in
        (HSFrag(f),state)

type recv_reply = 
  | HSAck of hs_state      (* fragment accepted, no visible effect so far *)
  | HSChangeVersion of hs_state * role * ProtocolVersionType 
                          (* ..., and we should use this new protocol version for sending *) 
  | HSFinished of hs_state (* ..., and we can start sending data on the connection *)

let makeHSPacket ht data =
    let htb = bytes_of_hs_type ht in
    let len = length data in
    let blen = bytes_of_int 3 len in
    appendList [htb; blen; data]

let makeHelloRequestBytes () =
    makeHSPacket HT_hello_request empty_bstr

let makeTimestamp () = (* FIXME: we may need to abstract this function *)
    let t = (System.DateTime.UtcNow - new System.DateTime(1970, 1, 1))
    (int) t.TotalSeconds

let makeCHello poptions session =
    let random = { time = makeTimestamp ();
                   rnd = mkRandom 28} in
    {
    client_version = poptions.maxVer
    ch_random = random
    ch_session_id = session
    cipher_suites = poptions.ciphersuites
    compression_methods = poptions.compressions
    extensions = empty_bstr
    }

let rec b_of_cslist cslist acc =
    match cslist with
    | [] -> vlenBytes_of_bytes 2 acc
    | h::t ->
        let csb = bytes_of_cipherSuite h in
        let acc = append acc csb in
        b_of_cslist t acc

let bytes_of_cipherSuites cslist =
    b_of_cslist cslist empty_bstr

let rec b_of_complist complist acc =
    match complist with
    | [] -> vlenBytes_of_bytes 1 acc
    | h::t ->
        let compb = bytes_of_compression h in
        let acc = append acc compb in
        b_of_complist t acc

let bytes_of_compressionMethods complist =
    b_of_complist complist empty_bstr

let makeCHelloBytes poptions session =
    let cHello = makeCHello poptions session in
    let cVerB = bytes_of_protocolVersionType cHello.client_version in
    let tsbytes = bytes_of_int 4 cHello.ch_random.time in
    let random = append tsbytes cHello.ch_random.rnd in
    let csessB = vlenBytes_of_bytes 1 cHello.ch_session_id in
    let ccsuitesB = bytes_of_cipherSuites cHello.cipher_suites in
    let ccompmethB = bytes_of_compressionMethods cHello.compression_methods in
    let data = appendList [cVerB; random; csessB; ccsuitesB; ccompmethB; cHello.extensions] in
    makeHSPacket HT_client_hello data

let init_handshake info poptions =
    let role = getSessionRole info in
    match role with
    | ClientRole ->
        {hs_outgoing = makeCHelloBytes poptions empty_bstr
         ccs_outgoing = None
         hs_outgoing_after_ccs = empty_bstr
         hs_incoming = empty_bstr
         hs_info = info
         poptions = poptions
         pstate = Client (ServerHello)}
    | ServerRole ->
        {hs_outgoing = empty_bstr
         ccs_outgoing = None
         hs_outgoing_after_ccs = empty_bstr
         hs_incoming = empty_bstr
         hs_info = info
         poptions = poptions
         pstate = Server (ClientHello)}

(*
let rehandshake hs_state use_resumption =
    match hs_state.pstate with
    | Client (CIdle) ->
        (* Many invariants should hold, like outgoing and ingoing buffers are empty... *)
        if use_resumption then
            correct (  {hs_state with hs_outgoing = makeCHelloBytes hs_state.poptions hs_state.session
                                      pstate = Client (ServerHello)} )
        else
            correct ( {hs_state with hs_outgoing = makeCHelloBytes hs_state.poptions hs_state.session
                                     session = empty_bstr
                                     pstate = Client (ServerHello)} )
    | Server (SIdle) ->
        (* ...many invariants should hold as well *)
        (* use_resumption value is ignored *)
        correct ( {hs_state with hs_outgoing = makeHelloRequestBytes ()
                                 session = empty_bstr
                                 pstate = Server (ClientHello)} )
    | _ -> Error (HandshakeProto, InvalidState)
*)

let parse_fragment hs_state fragment =
    (* Inefficient but simple implementation:
       every time we get a new fragment, we reparse the whole received
       packet, until a full packet is received. When a full packet is received,
       it is removed from the buffer.
       This algorithm can be easily made more efficient, but requires a more
       complex code *)
    let new_inc = append hs_state.hs_incoming fragment in
    if length new_inc < 4 then
        (* Not enough data to even start parsing *)
        let hs_state = {hs_state with hs_incoming = new_inc} in
        (hs_state, None)
    else
        let (hstypeb,rem) = split new_inc 1 in
        let (lenb,rem) = split rem 3 in
        let len = int_of_bytes 3 lenb in
        if length rem < len then
            (* not enough payload, try next time *)
            let hs_state = {hs_state with hs_incoming = new_inc} in
            (hs_state, None)
        else
            let hstype = hs_type_of_bytes hstypeb in
            let (payload,rem) = split rem len in
            let hs_state = { hs_state with hs_incoming = rem } in
            (hs_state, Some(hstype,payload))
            

let recv_fragment (hs_state:hs_state) (fragment:fragment) : recv_reply Result =
    let (hs_state,new_packet) = parse_fragment hs_state fragment in
    match new_packet with
    | None -> correct (HSAck(hs_state))
    | Some (data) ->
    let (hstype,payload) = data in
    match hs_state.pstate with
    | Client (cState) ->
        match cState with
        | ServerHello -> (* We're only willing to receive a ServerHello Message *)
            match hstype with
            | HT_server_hello -> (* TODO *) correct (HSAck(hs_state))
            | _ -> Error(HandshakeProto,CheckFailed)
        | _ -> Error (HandshakeProto,Unsupported)
    | Server (sState) -> Error (HandshakeProto,Unsupported)

let recv_ccs (hs_state: hs_state) (fragment:fragment): (hs_state * ccs_data) Result =
    Error (HandshakeProto,Unsupported)