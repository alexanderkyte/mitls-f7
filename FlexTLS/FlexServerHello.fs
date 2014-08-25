﻿#light "off"

module FlexServerHello

open Tcp
open Bytes
open Error
open System
open System.IO
open TLS
open TLSInfo
open TLSConstants
open TLSExtensions
open HandshakeMessages

open FlexTypes
open FlexConstants
open FlexState
open FlexHandshake




(* Inference on user provided information *)
let fillFServerHelloANDSi (fsh:FServerHello) (si:SessionInfo) : FServerHello * SessionInfo =

    (* rand = Is there random bytes ? If no, create some *)
    let rand =
        match fsh.rand = nullFServerHello.rand with
        | false -> fsh.rand
        | true -> Nonce.mkHelloRandom()
    in

    (* Update fch with correct informations and sets payload to empty bytes *)
    let fsh = { fsh with 
                rand = rand;
                payload = empty_bytes 
              } 
    in

    (* Update si with correct informations from fsh *)
    let si = { si with
               protocol_version = fsh.pv;
               sessionID = fsh.sid;
               cipher_suite = fsh.suite;
               compression = fsh.comp;
               init_srand = fsh.rand;
             } 
    in
    (fsh,si)

(* Update channel's Epoch Init Protocol version to the one chosen by the user if we are in an InitEpoch, else do nothing *)
let fillStateEpochInitPvIFIsEpochInit (st:state) (fsh:FServerHello) : state =
    if TLSInfo.isInitEpoch st.read.epoch then
        let st = FlexState.updateIncomingRecordEpochInitPV st fsh.pv in
        let st = FlexState.updateOutgoingRecordEpochInitPV st fsh.pv in
        st
    else
        st




type FlexServerHello = 
    class

    (* Receive a ServerHello message from the network stream *)
    static member receive (st:state, ?onsc:nextSecurityContext) : state * nextSecurityContext * FServerHello =
        
        let nsc = defaultArg onsc nullNextSecurityContext in
        let si = nsc.si in
        let st,hstype,payload,to_log = FlexHandshake.getHSMessage(st) in
        match hstype with
        | HT_server_hello  ->    
            (match parseServerHello payload with
            | Error (ad,x) -> failwith x
            | Correct (pv,sr,sid,cs,cm,extensions) ->
                let si  = { si with 
                            init_srand = sr;
                            protocol_version = pv;
                            sessionID = sid;
                            cipher_suite = cs;
                            compression = cm;
                } in
                let nsc = { nullNextSecurityContext with si = si } in
                let fsh = { nullFServerHello with 
                            pv = pv;
                            rand = sr;
                            sid = sid;
                            suite = cs;
                            comp = cm;
                            ext = extensions;
                            payload = to_log;
                } in
                let st = fillStateEpochInitPvIFIsEpochInit st fsh in
                (st,nsc,fsh)
            )
        | _ -> failwith "recvServerHello : message type should be HT_server_hello"
        
        
    (* Send a ServerHello message to the network stream *)
    static member send (st:state, ?onsc:nextSecurityContext, ?ofsh:FServerHello, ?ofp:fragmentationPolicy) : state * nextSecurityContext * FServerHello =
    
        let ns = st.ns in
        let fp = defaultArg ofp defaultFragmentationPolicy in
        let fsh = defaultArg ofsh nullFServerHello in
        let nsc = defaultArg onsc nullNextSecurityContext in
        let si = nsc.si in

        let fsh,si = fillFServerHelloANDSi fsh si in
        let st = fillStateEpochInitPvIFIsEpochInit st fsh in

        let msgb = HandshakeMessages.serverHelloBytes si fsh.rand fsh.ext in
        let st = FlexHandshake.send(st,HT_server_hello,msgb,fp) in
        let nsc = { nsc with si = si } in
        (* !!! BB !!! should be payload = payload but here we don't have it back, we only have access to the message bytes *)
        let fsh = { fsh with payload = msgb } in
        st,nsc,fsh


    end
