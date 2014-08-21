﻿#light "off"

module FlexAlert

open Alert
open Error
open TLSInfo
open TLSError
open FlexTypes
open FlexFragment



type FlexAlert = 
    class
    
    (* Receive an expected ServerHelloDone message from the network stream *)
    static member receive (st:state) : state * alertDescription =
        
        let abuf = st.read_s.alert_buffer in
        let st,alb,abuf = FlexFragment.getAlertMessage st abuf in
    
        match Alert.parseAlert alb with
        | Error(ad,x) -> failwith x
        | Correct(aldt) -> 
            let read_s = {st.read_s with alert_buffer = abuf } in
            let st = {st with read_s = read_s } in
            st,aldt

    (*
    (* Send ServerHelloDone message to the network stream *)
    static member send (st:state) (si:SessionInfo) (ad:alertDescription) : state * bytes =
    
        let ns = st.ns in
        let msgb = alertBytes ad in

        let len = length msgb in
        let rg : Range.range = (len,len) in

        let id = TLSInfo.id st.write_s.epoch in
        let frag = TLSFragment.fragment id Alert rg msgb in
        let nst,b = Record.recordPacketOut st.write_s.epoch st.write_s.record si.protocol_version rg Alert frag in
        let wst = {st.write_s with record = nst} in
        let st = {st with write_s = wst} in

        let fal = { fal with payload = b } in

        match Tcp.write ns b with
        | Error(x) -> failwith x
        | Correct() -> st,fshd
    *)
    
    end
