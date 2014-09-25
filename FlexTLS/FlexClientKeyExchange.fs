﻿#light "off"

module FlexClientKeyExchange

open Bytes
open Error
open TLSConstants
open TLSInfo
open HandshakeMessages

open FlexTypes
open FlexConstants
open FlexHandshake
open FlexSecrets




/// <summary>
/// Retreive certificate and private key associated to a certificate list
/// </summary>
/// <param name="osk"> Secret key option </param>
/// <param name="cfg"> Certificate list </param>
/// <returns> Secret key </returns>
let defaultKey osk certl =
    match osk with
    | Some(sk) -> sk
    | None ->
        let hint = 
            match Cert.get_hint certl with
            | None -> failwith (perror __SOURCE_FILE__ __LINE__ "Please provide a certificate")
            | Some(ch) -> ch
        in
        match Cert.for_key_encryption FlexConstants.sigAlgs_RSA hint with
        | None -> failwith (perror __SOURCE_FILE__ __LINE__ "Please provide a certificate for which the private key is available")
        | Some(c,sk) -> sk




type FlexClientKeyExchange =
    class

    /// <summary>
    /// Receive RSA ClientKeyExchange from the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="nsc"> Next security context being negociated </param>
    /// <param name="fch"> Client hello containing the desired protocol version when beginning the handshake </param>
    /// <param name="checkPV"> Optional check of the protocol version at decryption time </param>
    /// <param name="sk"> Optional secret key to be used for decrypting in place of the current one </param>
    /// <returns> Updated state * Updated next securtity context * FClientKeyExchange message record </returns>
    static member receiveRSA (st:state, nsc:nextSecurityContext, fch:FClientHello, ?checkPV:bool, ?sk:RSAKey.sk) : state * nextSecurityContext * FClientKeyExchange =
        let checkPV = defaultArg checkPV true in
        let sk = defaultKey sk nsc.si.serverID in
        FlexClientKeyExchange.receiveRSA(st,nsc,fch.pv,checkPV,sk)

    /// <summary>
    /// Receive RSA ClientKeyExchange from the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="nsc"> Next security context being negociated </param>
    /// <param name="pv"> Protocol version required in the Client Hello of the current Handshake </param>
    /// <param name="checkPV"> Optional check of the protocol version at decryption time </param>
    /// <param name="sk"> Optional secret key to be used for decrypting in place of the current one </param>
    /// <returns> Updated state * Updated next securtity context * FClientKeyExchange message record </returns>
    static member receiveRSA (st:state, nsc:nextSecurityContext, pv:ProtocolVersion, ?checkPV:bool, ?sk:RSAKey.sk) : state * nextSecurityContext * FClientKeyExchange =
        let checkPV = defaultArg checkPV true in
        let sk = defaultKey sk nsc.si.serverID in
        let st,fcke = FlexClientKeyExchange.receiveRSA(st,nsc.si.serverID,pv,checkPV,sk) in
        let epk = {nsc.keys with kex = fcke.kex} in
        let nsc = {nsc with keys = epk} in
        let nsc = FlexSecrets.fillSecrets(st,Server,nsc) in
        st,nsc,fcke

    /// <summary>
    /// Receive RSA ClientKeyExchange from the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="certl"> Server identification as list of certificates </param>
    /// <param name="pv"> Protocol version required in the Client Hello of the current Handshake </param>
    /// <param name="checkPV"> Optional check of the protocol version at decryption time </param>
    /// <param name="sk"> Optional secret key to be used for decrypting in place of the current one </param>
    /// <returns> Updated state * FClientKeyExchange message record </returns>
    static member receiveRSA (st:state, certl:list<Cert.cert>, pv:ProtocolVersion, ?checkPV:bool, ?sk:RSAKey.sk): state * FClientKeyExchange =
        let checkPV = defaultArg checkPV true in
        let sk = defaultKey sk certl in
        let pk = 
            match Cert.get_public_encryption_key certl.Head with
            | Error(_,x) -> failwith (perror __SOURCE_FILE__ __LINE__ x)
            | Correct(pk) -> pk
        in
        let si = {FlexConstants.nullSessionInfo with serverID = certl; protocol_version = pv} in
        let st,hstype,payload,to_log = FlexHandshake.getHSMessage(st) in
        match hstype with
        | HT_client_key_exchange  ->
            (match parseClientKeyExchange_RSA si payload with
            | Error (_,x) -> failwith (perror __SOURCE_FILE__ __LINE__ x)
            | Correct (encPMS) ->
                    let pmsa = RSA.decrypt sk si si.protocol_version checkPV encPMS in
                    let pmsb = PMS.leakRSA pk si.protocol_version pmsa in
                    let kex = RSA(pmsb) in
                    let fcke : FClientKeyExchange = {kex = kex; payload = to_log } in
                    st,fcke
            )
        | _ -> failwith (perror __SOURCE_FILE__ __LINE__  "message type should be HT_client_key_exchange")
      

    /// <summary>
    /// Prepare RSA ClientKeyExchange but will not send it to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="nsc"> Next security context being negociated </param>
    /// <param name="fch"> Client hello containing the desired protocol version </param>
    /// <returns> FClientKeyExchange bytes * Updated state * FClientKeyExchange message record </returns>
    static member prepareRSA (st:state, nsc:nextSecurityContext, fch:FClientHello): bytes * state * nextSecurityContext * FClientKeyExchange =
        FlexClientKeyExchange.prepareRSA(st,nsc,fch.pv)

    /// <summary>
    /// Prepare RSA ClientKeyExchange but will not send it to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="nsc"> Next security context being negociated </param>
    /// <param name="pv"> Protocol version required in the Client Hello of the current Handshake </param>
    /// <returns> FClientKeyExchange bytes * Updated state * FClientKeyExchange message record </returns>
    static member prepareRSA (st:state, nsc:nextSecurityContext, pv:ProtocolVersion): bytes * state * nextSecurityContext * FClientKeyExchange =
        let payload,st,fcke =
            match nsc.keys.kex with
            | RSA(pms) ->
                if pms = empty_bytes then
                    FlexClientKeyExchange.prepareRSA(st,nsc.si.serverID,pv)
                else
                    FlexClientKeyExchange.prepareRSA(st,nsc.si.serverID,pv,pms)
            | _ -> failwith (perror __SOURCE_FILE__ __LINE__ "RSA kex expected")
        in
        let epk = {nsc.keys with kex = fcke.kex} in
        let nsc = {nsc with keys = epk} in
        let nsc = FlexSecrets.fillSecrets(st,Client,nsc) in
        payload,st,nsc,fcke

    /// <summary>
    /// Prepare RSA ClientKeyExchange but will not send it to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="certl"> Server identification as list of certificates </param>
    /// <param name="pv"> Protocol version required in the Client Hello of the current Handshake </param>
    /// <param name="pms"> Optional Pre Master Secret to be prepared instead of the real one // BB : ?? </param>
    /// <returns> FClientKeyExchange bytes * Updated state * FClientKeyExchange message record </returns>
    static member prepareRSA (st:state, certl:list<Cert.cert>, pv:ProtocolVersion, ?pms:bytes) : bytes * state * FClientKeyExchange =
        if certl.IsEmpty then
            failwith (perror __SOURCE_FILE__ __LINE__  "Server certificate should always be present with a RSA signing cipher suite.")
        else
            match Cert.get_chain_public_encryption_key certl with
            | Error(_,x) -> failwith (perror __SOURCE_FILE__ __LINE__ x)
            | Correct(pk) ->
                let si = {FlexConstants.nullSessionInfo with
                            protocol_version = pv;
                            serverID = certl} in
                let pms,pmsb =
                    match pms with
                    | Some(pmsb) -> (PMS.coerceRSA pk pv pmsb),pmsb
                    | None ->
                        let pms = (PMS.genRSA pk pv) in
                        pms,PMS.leakRSA pk pv pms
                in
                let encpms = RSA.encrypt pk pv pms in
                let payload = clientKeyExchangeBytes_RSA si encpms in
                let kex = RSA(pmsb) in
                let fcke : FClientKeyExchange = {kex = kex; payload = payload } in
                payload,st,fcke

    /// <summary>
    /// Send RSA ClientKeyExchange to the network stream,
    /// compute the PMS, MS, KEYS and update the nextSecurityContext with the protocol version
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="nsc"> Next security context being negociated </param>
    /// <param name="fch"> Client hello containing the desired protocol version </param>
    /// <param name="fp"> Optional fragmentation policy at the record level </param>
    /// <returns> Updated state * FClientKeyExchange message record </returns>
    static member sendRSA (st:state, nsc:nextSecurityContext, fch:FClientHello, ?fp:fragmentationPolicy): state * nextSecurityContext * FClientKeyExchange =
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in
        FlexClientKeyExchange.sendRSA(st,nsc,fch.pv,fp)

    /// <summary>
    /// Overload : Send RSA ClientKeyExchange to the network stream,
    /// compute the PMS, MS, KEYS and update the nextSecurityContext with the protocol version
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="nsc"> Next security context being negociated </param>
    /// <param name="pv"> Protocol version required in the Client Hello of the current Handshake </param>
    /// <param name="fp"> Optional fragmentation policy at the record level </param>
    /// <returns> Updated state * FClientKeyExchange message record </returns>
    static member sendRSA (st:state, nsc:nextSecurityContext, pv:ProtocolVersion, ?fp:fragmentationPolicy): state * nextSecurityContext * FClientKeyExchange =
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in
        let st,fcke =
            match nsc.keys.kex with
            | RSA(pms) ->
                if pms = empty_bytes then
                    FlexClientKeyExchange.sendRSA(st,nsc.si.serverID,pv,fp=fp)
                else
                    FlexClientKeyExchange.sendRSA(st,nsc.si.serverID,pv,pms,fp)
            | _ -> failwith (perror __SOURCE_FILE__ __LINE__ "RSA kex expected")
        in
        let epk = {nsc.keys with kex = fcke.kex} in
        let nsc = {nsc with keys = epk} in
        let nsc = FlexSecrets.fillSecrets(st,Client,nsc) in
        st,nsc,fcke

    /// <summary>
    /// Send RSA ClientKeyExchange to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="certl"> Server identification as list of certificates </param>
    /// <param name="pv"> Protocol version required in the Client Hello of the current Handshake </param>
    /// <param name="pms"> Optional Pre Master Secret to be sent instead of the real one </param>
    /// <param name="fp"> Optional fragmentation policy at the record level </param>
    /// <returns> Updated state * FClientKeyExchange message record </returns>
    static member sendRSA (st:state, certl:list<Cert.cert>, pv:ProtocolVersion, ?pms:bytes, ?fp:fragmentationPolicy) : state * FClientKeyExchange =
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in
        if certl.IsEmpty then
            failwith (perror __SOURCE_FILE__ __LINE__  "Server certificate should always be present with a RSA signing cipher suite.")
        else
            match Cert.get_chain_public_encryption_key certl with
            | Error(_,x) -> failwith (perror __SOURCE_FILE__ __LINE__ x)
            | Correct(pk) ->
                let si = {FlexConstants.nullSessionInfo with
                            protocol_version = pv;
                            serverID = certl} in
                let pms,pmsb =
                    match pms with
                    | Some(pmsb) -> (PMS.coerceRSA pk pv pmsb),pmsb
                    | None ->
                        let pms = (PMS.genRSA pk pv) in
                        pms,PMS.leakRSA pk pv pms
                in
                let encpms = RSA.encrypt pk pv pms in
                let payload = clientKeyExchangeBytes_RSA si encpms in
                let st = FlexHandshake.send(st,payload,fp) in
                let kex = RSA(pmsb) in
                let fcke : FClientKeyExchange = {kex = kex; payload = payload } in
                st,fcke


    (*----------------------------------------------------------------------------------------------------------------------------------------------*)
    // TODO BB : The RSA and DHE methods should be fixed to all use the kex record instead of pms and kexDH

    /// <summary>
    /// Receive DHE ClientKeyExchange fromthe network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="fske"> Previously sent Server Key exchange containing kex record and DH parameters </param>
    /// <param name="nsc"> Next security context being negociated and containing the key exchange mechanism retreived from a previous server key exchange</param>
    /// <returns> Updated state * Next security context * FClientKeyExchange message record </returns>
    // FIXME BB : Not sure about this ! Should we override the next security context with a FServerKeyExchangeDH
    static member receiveDHE (st:state, fske:FServerKeyExchange, ?nsc:nextSecurityContext) : state * nextSecurityContext * FClientKeyExchange =
        let nsc = defaultArg nsc FlexConstants.nullNextSecurityContext in
        let epk = {nsc.keys with kex = fske.kex} in
        let nsc = {nsc with keys = epk} in
        FlexClientKeyExchange.receiveDHE(st,nsc)

    /// <summary>
    /// Receive DHE ClientKeyExchange fromthe network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="nsc"> Next security context being negociated and containing the key exchange mechanism retreived from a previous server key exchange</param>
    /// <returns> Updated state * Next security context * FClientKeyExchange message record </returns>
    static member receiveDHE (st:state, nsc:nextSecurityContext) : state * nextSecurityContext * FClientKeyExchange =
        let kexdh =
            match nsc.keys.kex with
            | DH(kexdh) -> kexdh
            | _         -> failwith (perror __SOURCE_FILE__ __LINE__  "key exchange mechanism should be DHE")
        in
        let st,fcke = FlexClientKeyExchange.receiveDHE(st,kexdh) in
        let epk = {nsc.keys with kex = fcke.kex} in
        let nsc = {nsc with keys = epk} in
        let nsc = FlexSecrets.fillSecrets(st,Server,nsc) in
        st,nsc,fcke

    /// <summary>
    /// Receive DHE ClientKeyExchange from the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="kexdh"> Key Exchange record containing Diffie-Hellman parameters </param>
    /// <returns> Updated state * FClientKeyExchange message record </returns>
    static member receiveDHE (st:state, kexdh:kexDH) : state * FClientKeyExchange =
        let (p,g),gx = kexdh.pg,kexdh.gx in
        let dhp = {FlexConstants.nullDHParams with dhp = p; dhg = g} in
        let st,hstype,payload,to_log = FlexHandshake.getHSMessage(st) in
        match hstype with
        | HT_client_key_exchange  ->
            (match HandshakeMessages.parseClientKEXExplicit_DH dhp payload with
            | Error(ad,err) -> failwith err
            | Correct(gy) ->
                let kexdh = { kexdh with gy = gy } in
                let fcke = { kex = DH(kexdh); payload = to_log } in
                st,fcke
            )
        | _ -> failwith (perror __SOURCE_FILE__ __LINE__  "message type should be HT_client_key_exchange")
        

    /// <summary>
    /// Prepare DHE ClientKeyExchange bytes but will not send it to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="kexdh"> Key Exchange record containing necessary Diffie-Hellman parameters </param>
    /// <returns> Updated state * FClientKeyExchange message record </returns>
    static member prepareDHE (st:state, kexdh:kexDH) : bytes * state * FClientKeyExchange =        
        let p,g = kexdh.pg in
        let dhp = { FlexConstants.nullDHParams with dhp = p; dhg = g } in
        let x,gx = CoreDH.gen_key_pg p g in
        let payload = clientKEXExplicitBytes_DH gx in
        let kexdh = { kexdh with x = x; gx = gx } in
        let fcke = { kex = DH(kexdh); payload = payload } in
        payload,st,fcke

    /// <summary>
    /// Overload : Send DHE ClientKeyExchange to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="fske"> Server key exchange data necessary or a modified version </param>
    /// <param name="fp"> Optional fragmentation policy at the record level </param>
    /// <returns> Updated state * Next security context * FClientKeyExchange message record </returns>
    // FIXME BB : Not sure about this ! Should we override the next security context with a FServerKeyExchangeDH ?
    static member sendDHE (st:state, fske:FServerKeyExchange, ?nsc:nextSecurityContext, ?fp:fragmentationPolicy) : state * nextSecurityContext * FClientKeyExchange =
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in
        let nsc = defaultArg nsc FlexConstants.nullNextSecurityContext in
        let epk = {nsc.keys with kex = fske.kex} in
        let nsc = {nsc with keys = epk} in
        FlexClientKeyExchange.sendDHE(st,nsc,fp)

    /// <summary>
    /// Overload : Send DHE ClientKeyExchange to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="nsc"> Next security context being negociated and containing the key exchange mechanism retreived from a previous server key exchange</param>
    /// <param name="fp"> Optional fragmentation policy at the record level </param>
    /// <returns> Updated state * Next security context * FClientKeyExchange message record </returns>
    static member sendDHE (st:state, nsc:nextSecurityContext, ?fp:fragmentationPolicy) : state * nextSecurityContext * FClientKeyExchange =
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in
        let kexdh =
            match nsc.keys.kex with
            | DH(kexdh) -> kexdh
            | _         -> failwith (perror __SOURCE_FILE__ __LINE__  "key exchange mechanism should be DHE")
        in
        let st,fcke = FlexClientKeyExchange.sendDHE(st,kexdh,fp) in
        let epk = { nsc.keys with kex = fcke.kex } in
        let nsc = { nsc with keys = epk } in
        let nsc = FlexSecrets.fillSecrets(st,Client,nsc) in
        st,nsc,fcke

    /// <summary>
    /// Send DHE ClientKeyExchange to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="kexdh"> Key Exchange record containing necessary Diffie-Hellman parameters </param>
    /// <param name="fp"> Optional fragmentation policy at the record level </param>
    /// <returns> Updated state * FClientKeyExchange message record </returns>
    static member sendDHE (st:state, kexdh:kexDH, ?fp:fragmentationPolicy) : state * FClientKeyExchange =
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in
        
        let p,g = kexdh.pg in
        let dhp = { FlexConstants.nullDHParams with dhp = p; dhg = g } in
        let x,gx = CoreDH.gen_key_pg p g in

        let payload = clientKEXExplicitBytes_DH gx in
        let st = FlexHandshake.send(st,payload,fp) in
        let kexdh = { kexdh with x = x; gx = gx } in
        let fcke = { kex = DH(kexdh); payload = payload } in
        st,fcke

    end




type FlexClientKeyExchangeTLS13 =
    class

    /// <summary>
    /// EXPERIMENTAL TLS 1.3 Receive DHE ClientKeyExchange from the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <returns> Updated state * FClientKeyExchangeTLS13 message record </returns>
    static member receive (st:state) : state * FClientKeyExchangeTLS13 =
        let st,hstype,payload,to_log = FlexHandshake.getHSMessage(st) in
        match hstype with
        | HT_client_key_exchange  ->
            (match HandshakeMessages.parseTLS13CKEOffers payload with
            | Error(_,x) -> failwith x
            | Correct(kexl) ->
                let fcke = { offers = kexl ; payload = to_log } in
                st,fcke
            )
        | _ -> failwith (perror __SOURCE_FILE__ __LINE__  "message type should be HT_client_key_exchange")

    /// <summary>
    /// EXPERIMENTAL TLS 1.3 Send DHE ClientKeyExchange to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="kexl"> Key Exchange record containing necessary Diffie-Hellman parameters </param>
    /// <param name="fp"> Optional fragmentation policy at the record level </param>
    /// <returns> Updated state * FClientKeyExchangeTLS13 message record </returns>
    static member send (st:state, ?kexl:list<tls13kex>, ?fp:fragmentationPolicy) : state * FClientKeyExchangeTLS13 =
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in
        // FIXME BB : default group and bytes should not be empty !!!!
        let kexl = defaultArg kexl [DHE(FlexConstants.defaultTLS13group, empty_bytes)] in
        let payload = HandshakeMessages.tls13CKEOffersBytes kexl in
        let st = FlexHandshake.send(st,payload,fp) in
        let fcke = { offers = kexl ; payload = payload } in
        st,fcke

    end
