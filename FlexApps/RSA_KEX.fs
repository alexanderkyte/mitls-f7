﻿#light "off"

module RSA_KEX

open Tcp
open Bytes
open Error
open TLS
open TLSInfo
open TLSConstants

open FlexTypes
open FlexConstants
open FlexConnection
open FlexClientHello
open FlexServerHello
open FlexCertificate
open FlexCertificateRequest
open FlexCertificateVerify
open FlexServerHelloDone
open FlexClientKeyExchange
open FlexCCS
open FlexFinished
open FlexState
open FlexSecrets

(* Get the client certificate *)
(* let chain,algs,skey =
    let hint = "*.prosecco.fr" in
    match Cert.for_signing calgs_rsa hint calgs_rsa with
    | None -> failwith "Frack"
    | Some(c,a,s) -> c,a,s
in *)

type RSA_KEX =
    class

    (* Run a full Handshake RSA with server side authentication only *)
    static member client (server_name:string, ?port:int) : unit =
        let port = defaultArg port FlexConstants.defaultTCPPort in

        // Start TCP connection with the server
        let st,_ = FlexConnection.clientOpenTcpConnection(server_name,server_name,port) in

        // Typical RSA key exchange messages

        // Ensure we use RSA
        let ch = {nullFClientHello with
            suites = [TLS_RSA_WITH_AES_128_CBC_SHA] } in
        let st,nsc,ch   = FlexClientHello.send(st,ch) in
        let st,nsc,sh   = FlexServerHello.receive(st,nsc) in
        let st,nsc,cert = FlexCertificate.receive(st,Client,nsc) in
        let st,shd      = FlexServerHelloDone.receive(st) in
        let st,nsc,cke  = FlexClientKeyExchange.sendRSA(st,nsc,ch) in
        let st,_        = FlexCCS.send(st) in
            
        // Start encrypting
        let st           = FlexState.installWriteKeys st nsc in
        let log          = ch.payload @| sh.payload @| cert.payload @| shd.payload @| cke.payload in
            
        let st,fC        = FlexFinished.send(st, logRoleNSC=(log,Client,nsc)) in
        let st,_     = FlexCCS.receive(st) in

        // Start decrypting
        let st           = FlexState.installReadKeys st nsc in

        let log          = log @| fC.payload in
        let verify_data  = FlexSecrets.makeVerifyData nsc.si nsc.ms Server log in
        let st,fS        = FlexFinished.receive(st) in
            
        // Check match of reveived log with the correct one
        if not (verify_data = fS.verify_data)
        then
            failwith (perror __SOURCE_FILE__ __LINE__ "Server verify_data doesn't match")
        else
        ()

    static member server (listening_address:string, ?cn:string, ?port:int) : unit =
        let cn = defaultArg cn listening_address in
        let port = defaultArg port FlexConstants.defaultTCPPort in
        match Cert.for_key_encryption FlexConstants.calgs_RSA cn with
        | None -> failwith (perror __SOURCE_FILE__ __LINE__ (sprintf "Private key not found for the given CN: %s" cn))
        | Some(chain,sk) -> RSA_KEX.server(listening_address,chain,sk,port)

    static member server (listening_address:string, chain:Cert.chain, sk:RSAKey.sk, ?port:int) : unit =
        let port = defaultArg port FlexConstants.defaultTCPPort in
        let cn =
            match Cert.get_hint chain with
            | None -> failwith (perror __SOURCE_FILE__ __LINE__ "Could not parse given certficate")
            | Some(cn) -> cn
        in

        // Accept TCP connection from the client
        let st,cfg = FlexConnection.serverOpenTcpConnection(listening_address,cn,port) in

        // Start typical RSA key exchange
        let st,nsc,ch   = FlexClientHello.receive(st) in

        // Sanity check: our preferred ciphersuite is there
        if not (List.exists (fun cs -> cs = TLS_RSA_WITH_AES_128_CBC_SHA) ch.suites) then
            failwith (perror __SOURCE_FILE__ __LINE__ "No suitable ciphersuite given")
        else

        // Ensure we send our preferred ciphersuite
        let sh = {nullFServerHello with suite = TLS_RSA_WITH_AES_128_CBC_SHA} in
        let st,nsc,sh   = FlexServerHello.send(st,nsc,sh) in
        let st,nsc,cert = FlexCertificate.send(st,Server,chain,nsc) in
        let st,shd      = FlexServerHelloDone.send(st) in
        let st,nsc,cke  = FlexClientKeyExchange.receiveRSA(st,nsc,ch,sk=sk) in
        let st,_        = FlexCCS.receive(st) in

        let log = ch.payload @| sh.payload @| cert.payload @| shd.payload @| cke.payload in
        let verify_data = FlexSecrets.makeVerifyData nsc.si nsc.ms Client log in

        // Start decrypting
        let st          = FlexState.installReadKeys st nsc in
        let st,fC       = FlexFinished.receive(st) in

        // Check verify_data
        if not (verify_data = fC.verify_data) then
            failwith (perror __SOURCE_FILE__ __LINE__ "Client verify_data doesn't match")
        else

        let st,_   = FlexCCS.send(st) in
            
        // Start encrypting
        let st     = FlexState.installWriteKeys st nsc in
        let log    = log @| fC.payload in
        let _      = FlexFinished.send(st,logRoleNSC=(log,Server,nsc)) in
        ()

    (* Run a full Handshake RSA with both server and client authentication *)
    static member full_handshake_RSA_with_client_auth (role:Role) (st:state) (chain:Cert.chain) (salg:Sig.alg) (skey:Sig.skey) : state * FHSMessages =
    let sms = nullFHSMessages in

    match role with
    | Client -> 
        let st,nsc,fch   = FlexClientHello.send(st) in
        let st,nsc,fsh   = FlexServerHello.receive(st,nsc) in
        let st,nsc,fcert = FlexCertificate.receive(st,role,nsc) in
        let st,nsc,fcreq = FlexCertificateRequest.receive(st,nsc) in
        let st,fshd      = FlexServerHelloDone.receive(st) in
            
        // Client authentication
        let st,nsc,fcertC = FlexCertificate.send(st,Client,chain,nsc) in
        let log          = fch.payload @| fsh.payload @| fcert.payload @| fcreq.payload @| fshd.payload @| fcertC.payload in
        let st,fcver     = FlexCertificateVerify.send(st,log,nsc.si.protocol_version,salg,skey) in
            
        let st,nsc,fcke  = FlexClientKeyExchange.sendRSA(st,nsc,fch) in
        let st,fccsC     = FlexCCS.send(st) in
            
        // Start encrypting
        let st           = FlexState.installWriteKeys st nsc in
            
        let log          = log @| fcver.payload @| fcke.payload in
        let st,ffC       = FlexFinished.send(st, logRoleNSC=(log,Client,nsc)) in
        let st,fccsS     = FlexCCS.receive(st) in

        // Start decrypting
        let st           = FlexState.installReadKeys st nsc in
            
        let log          = log @| ffC.payload in
        let verify_data  = FlexSecrets.FlexSecrets.makeVerifyData nsc.si nsc.ms role log in
        let st,ffS       = FlexFinished.receive(st) in
            
        // Check match of reveived log with the correct one
        if not (verify_data = ffS.verify_data) then failwith "Log message received doesn't match" else

        let sms = { sms with 
                    clientHello = fch; 
                    serverHello = fsh;
                    serverCertificate = fcert;
                    serverHelloDone = fshd;
                    clientKeyExchange = fcke;
                    clientChangeCipherSpecs = fccsC;
                    clientFinished = ffC;
                    serverChangeCipherSpecs = fccsS;
                    serverFinished = ffS;
                    } in
        (st,sms)

    | Server ->
        let st,nsc,fch   = FlexClientHello.receive(st) in
        let st,nsc,fsh   = FlexServerHello.send(st,nsc) in
        let st,nsc,fcert = FlexCertificate.send(st,role,chain,nsc) in
        let st,fcreq     = FlexCertificateRequest.send(st) in
        let st,fshd      = FlexServerHelloDone.send(st) in

        // Client authentication
        let st,nsc,fcertC = FlexCertificate.receive(st,Server,nsc) in
        let log          = fch.payload @| fsh.payload @| fcert.payload @| fcreq.payload @| fshd.payload @| fcertC.payload in
        let st,fcver     = FlexCertificateVerify.receive(st,nsc.si.protocol_version,fcreq.sigAlgs) in
        if not (log = fcver.payload) then failwith "Log message received doesn't match" else

        let st,nsc,fcke  = FlexClientKeyExchange.receiveRSA(st,nsc,fch.pv) in
        let st,fccsC     = FlexCCS.receive(st) in

        // Start decrypting
        let st,ffC       = FlexFinished.receive(st) in
        let st,fccsS     = FlexCCS.send(st) in

        // Start encrypting
        let log          = log @| fcver.payload @| fcke.payload @| fccsC.payload @| ffC.payload in
        let verify_data  = FlexSecrets.makeVerifyData nsc.si nsc.ms role log in

        // Check match of reveived log with the correct one
        if not (verify_data = ffC.verify_data) then failwith "Log message received doesn't match" else
            
        let st,ffS       = FlexFinished.send(st,verify_data) in
            
        let sms = { sms with 
                    clientHello = fch; 
                    serverHello = fsh;
                    serverCertificate = fcert;
                    serverHelloDone = fshd;
                    clientKeyExchange = fcke;
                    clientChangeCipherSpecs = fccsC;
                    clientFinished = ffC;
                    serverChangeCipherSpecs = fccsS;
                    serverFinished = ffS;
                    } in
        (st,sms)

    end