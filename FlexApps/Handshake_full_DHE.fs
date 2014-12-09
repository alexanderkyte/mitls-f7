﻿#light "off"

module FlexApps.Handshake_full_DHE

open Bytes
open Error
open TLSInfo
open TLSConstants

open FlexTLS
open FlexTypes
open FlexConstants
open FlexConnection
open FlexClientHello
open FlexServerHello
open FlexCertificate
open FlexServerKeyExchange
open FlexCertificateRequest
open FlexCertificateVerify
open FlexServerHelloDone
open FlexClientKeyExchange
open FlexCCS
open FlexFinished
open FlexState
open FlexSecrets
open FlexStatefulAPI




type Handshake_full_DHE =
    class

    (* CLIENT - Run a full Handshake DHE with server side authentication only *)
    static member client (server_name:string, ?port:int, ?st:state, ?timeout:int) : state =
        let port = defaultArg port FlexConstants.defaultTCPPort in
        let timeout = defaultArg timeout 0 in

        // Start TCP connection with the server if no state is provided by the user
        let st,_ = 
            match st with
            | None -> FlexConnection.clientOpenTcpConnection(server_name,server_name,port,timeout=timeout)
            | Some(st) -> st,TLSInfo.defaultConfig
        in

        // Typical DHE key exchange messages

        // Ensure we use DHE
        let fch = {FlexConstants.nullFClientHello with
            ciphersuites = Some([TLS_DHE_RSA_WITH_AES_128_CBC_SHA]) } in

        let st,nsc,fch   = FlexClientHello.send(st,fch) in
        let st,nsc,fsh   = FlexServerHello.receive(st,fch,nsc) in
        let st,nsc,fcert = FlexCertificate.receive(st,Client,nsc) in
        let st,nsc,fske  = FlexServerKeyExchange.receiveDHE(st,nsc) in
        let st,fshd      = FlexServerHelloDone.receive(st) in
        let st,nsc,fcke  = FlexClientKeyExchange.sendDHE(st,nsc) in
        let st,_         = FlexCCS.send(st) in
            
        // Start encrypting
        let st           = FlexState.installWriteKeys st nsc in
            
        let st,ffC       = FlexFinished.send(st,nsc,Client) in
        let st,_,_       = FlexCCS.receive(st) in

        // Start decrypting
        let st           = FlexState.installReadKeys st nsc in

        let st,ffS       = FlexFinished.receive(st,nsc,Server) in
        st
    

    (* CLIENT - Run a full Handshake DHE with both server side and client side authentication only *)
    static member client_with_auth (server_name:string, cn_hint:string, ?port:int, ?timeout:int) : state =
        let port = defaultArg port FlexConstants.defaultTCPPort in
        let timeout = defaultArg timeout 0 in
        let chain,salg,skey =
            match Cert.for_signing FlexConstants.sigAlgs_ALL cn_hint FlexConstants.sigAlgs_RSA with
            | None -> failwith "Failed to retreive certificate data"
            | Some(c,a,s) -> c,a,s
        in
        Handshake_full_DHE.client_with_auth (server_name,chain,salg,skey,port,timeout)

    static member client_with_auth (server_name:string, chain:Cert.chain, salg:Sig.alg, skey:Sig.skey, ?port:int, ?timeout:int) : state =
        let port = defaultArg port FlexConstants.defaultTCPPort in
        let timeout = defaultArg timeout 0 in

        // Start TCP connection with the server
        let st,_ = FlexConnection.clientOpenTcpConnection(server_name,server_name,port,timeout=timeout) in

        // Typical DHE key exchange messages

        // Ensure we use DHE
        let fch = {FlexConstants.nullFClientHello with
            ciphersuites = Some([TLS_DHE_RSA_WITH_AES_128_CBC_SHA]) } in

        let st,nsc,fch   = FlexClientHello.send(st,fch) in
        let st,nsc,fsh   = FlexServerHello.receive(st,fch,nsc) in
        let st,nsc,fcert = FlexCertificate.receive(st,Client,nsc) in
        let st,nsc,fske  = FlexServerKeyExchange.receiveDHE(st,nsc,minDHsize=(512,160)) in
        let st,nsc,fcreq = FlexCertificateRequest.receive(st,nsc) in
        let st,fshd      = FlexServerHelloDone.receive(st) in
            
        // Client authentication
        let st,nsc,fcertC = FlexCertificate.send(st,Client,chain,nsc) in
        let st,nsc,fcke  = FlexClientKeyExchange.sendDHE(st,nsc) in
        let st,fcver     = FlexCertificateVerify.send(st,nsc.si,salg,skey,nsc.keys.ms) in

        // Advertise that we will encrypt the trafic from now on
        let st,_         = FlexCCS.send(st) in
            
        // Start encrypting
        let st           = FlexState.installWriteKeys st nsc in
        
        let st,ffC       = FlexFinished.send(st,nsc,Client) in
        let st,_,_       = FlexCCS.receive(st) in

        // Start decrypting
        let st           = FlexState.installReadKeys st nsc in
            
        let st,ffS       = FlexFinished.receive(st,nsc,Server) in
        st

    (* SERVER - Run a full Handshake DHE with server side authentication only *)
    static member server (listening_address:string, ?cn_hint:string, ?port:int, ?timeout:int) : state =
        let cn_hint = defaultArg cn_hint listening_address in
        let timeout = defaultArg timeout 0 in
        let port = defaultArg port FlexConstants.defaultTCPPort in
        match Cert.for_signing FlexConstants.sigAlgs_ALL cn_hint FlexConstants.sigAlgs_RSA with
        | None -> failwith (perror __SOURCE_FILE__ __LINE__ (sprintf "Private key not found for the given CN: %s" cn_hint))
        | Some(chain,_,_) -> Handshake_full_DHE.server(listening_address,chain,port,timeout)

    static member server (listening_address:string, chain:Cert.chain, ?port:int, ?timeout:int) : state =
        let port = defaultArg port FlexConstants.defaultTCPPort in
        let timeout = defaultArg timeout 0 in
        let cn_hint =
            match Cert.get_hint chain with
            | None -> failwith (perror __SOURCE_FILE__ __LINE__ "Could not parse given certficate")
            | Some(cn_hint) -> cn_hint
        in

        // Accept TCP connection from the client
        let st,cfg = FlexConnection.serverOpenTcpConnection(listening_address,cn_hint,port,timeout=timeout) in

        // Start typical DHE key exchange
        let st,nsc,fch   = FlexClientHello.receive(st) in

        // Sanity check: our preferred ciphersuite is there
        if not (List.exists (fun cs -> cs = TLS_DHE_RSA_WITH_AES_128_CBC_SHA) (FlexClientHello.getCiphersuites fch)) then
            failwith (perror __SOURCE_FILE__ __LINE__ "No suitable ciphersuite given")
        else

        // Ensure we send our preferred ciphersuite
        let fsh = { FlexConstants.nullFServerHello with 
            ciphersuite = Some(TLS_DHE_RSA_WITH_AES_128_CBC_SHA)} in

        let st,nsc,fsh   = FlexServerHello.send(st,fch,nsc,fsh) in
        let st,nsc,fcert = FlexCertificate.send(st,Server,chain,nsc) in
        let st,nsc,fske  = FlexServerKeyExchange.sendDHE(st,nsc) in
        let st,fshd      = FlexServerHelloDone.send(st) in
        let st,nsc,fcke  = FlexClientKeyExchange.receiveDHE(st,nsc) in
        let st,_,_       = FlexCCS.receive(st) in

        // Start decrypting
        let st          = FlexState.installReadKeys st nsc in

        let st,ffC      = FlexFinished.receive(st,nsc,Client) in

        // Advertise we will encrypt traffic from now on
        let st,_   = FlexCCS.send(st) in
            
        // Start encrypting
        let st     = FlexState.installWriteKeys st nsc in
        let _      = FlexFinished.send(st,nsc,Server) in
        st


    (* SERVER - Run a full Handshake DHE with both server side and client side authentication only *)
    static member server_with_client_auth (listening_address:string, ?cn_hint:string, ?port:int, ?timeout:int) : state =
        let cn_hint = defaultArg cn_hint listening_address in
        let timeout = defaultArg timeout 0 in
        let port = defaultArg port FlexConstants.defaultTCPPort in
        match Cert.for_key_encryption FlexConstants.sigAlgs_RSA cn_hint with
        | None -> failwith (perror __SOURCE_FILE__ __LINE__ (sprintf "Private key not found for the given CN: %s" cn_hint))
        | Some(chain,sk) -> Handshake_full_DHE.server_with_client_auth(listening_address,chain,port,timeout)

    static member server_with_client_auth (listening_address:string, chain:Cert.chain, ?port:int, ?timeout:int) : state =
        let port = defaultArg port FlexConstants.defaultTCPPort in
        let timeout = defaultArg timeout 0 in
        let cn_hint =
            match Cert.get_hint chain with
            | None -> failwith (perror __SOURCE_FILE__ __LINE__ "Could not parse given certficate")
            | Some(cn_hint) -> cn_hint
        in

        // Accept TCP connection from the client
        let st,cfg = FlexConnection.serverOpenTcpConnection(listening_address,cn_hint,port,timeout=timeout) in

        // Start typical DHE key exchange
        let st,nsc,fch   = FlexClientHello.receive(st) in

        // Sanity check: our preferred ciphersuite and protovol version are there
        if ( not (List.exists (fun cs -> cs = TLS_DHE_RSA_WITH_AES_128_CBC_SHA) (FlexClientHello.getCiphersuites fch)) ) || (FlexClientHello.getPV fch) <> TLS_1p2 then
            failwith (perror __SOURCE_FILE__ __LINE__ "No suitable ciphersuite given")
        else

        // Ensure we send our preferred ciphersuite
        let sh = { FlexConstants.nullFServerHello with
            ciphersuite = Some(TLS_DHE_RSA_WITH_AES_128_CBC_SHA) } in

        let st,nsc,fsh   = FlexServerHello.send(st,fch,nsc,sh) in
        let st,nsc,fcert = FlexCertificate.send(st,Server,chain,nsc) in
        let st,nsc,fske  = FlexServerKeyExchange.sendDHE(st,nsc) in
        let st,fcreq     = FlexCertificateRequest.send(st,nsc) in
        let st,fshd      = FlexServerHelloDone.send(st) in

        // Client authentication
        let st,nsc,fcertC = FlexCertificate.receive(st,Server,nsc) in
        let st,nsc,fcke  = FlexClientKeyExchange.receiveDHE(st,nsc) in
        let st,fcver     = FlexCertificateVerify.receive(st,nsc,fcreq) in
        let st,_,_       = FlexCCS.receive(st) in

        // Start decrypting
        let st           = FlexState.installReadKeys st nsc in

        let st,ffC       = FlexFinished.receive(st,nsc,Client) in
        
        // Advertise that we will encrypt the trafic from now on
        let st,_         = FlexCCS.send(st) in

        // Start encrypting
        let st           = FlexState.installWriteKeys st nsc in

        let st,ffS       = FlexFinished.send(st,nsc,Server) in
        st

    end
