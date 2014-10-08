﻿#light "off"

module FlexTLS.FlexClientKeyShare

open NLog

open Bytes
open Error
open TLSConstants
open TLSInfo
open HandshakeMessages

open FlexTypes
open FlexConstants
open FlexHandshake
open FlexSecrets




type FlexClientKeyShare =
    class

    /// <summary>
    /// EXPERIMENTAL TLS 1.3 Receive DHE FClientKeyShare from the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <returns> Updated state * FClientKeyShare message record </returns>
    static member receive (st:state,nsc:nextSecurityContext) : state * nextSecurityContext * FClientKeyShare =
        LogManager.GetLogger("file").Info("# CLIENT KEY SHARE : FlexClientKeyShare.receive");
        let st,hstype,payload,to_log = FlexHandshake.getHSMessage(st) in
        match hstype with
        | HT_client_key_exchange  ->
            (match HandshakeMessages.parseTLS13CKEOffers payload with
            | Error(_,x) -> failwith (perror __SOURCE_FILE__ __LINE__ x)
            | Correct(kexl) ->
                let fcks = { offers = kexl ; payload = to_log } in
                let offers =
                    List.map (fun x ->
                        match x with
                        | DHE(g,gy) ->
                            LogManager.GetLogger("file").Debug(sprintf "--- Public Group : %A" g);
                            LogManager.GetLogger("file").Debug(sprintf "--- Public Exponent : %s" (Bytes.hexString(gy)));
                            DH13({group = g; x = empty_bytes; gx = empty_bytes; gy = gy})
                    ) kexl
                in
                let nsc = {nsc with offers = offers} in
                LogManager.GetLogger("file").Info(sprintf "--- Payload : %s" (Bytes.hexString(payload)));
                st,nsc,fcks
            )
        | _ -> failwith (perror __SOURCE_FILE__ __LINE__  "message type should be HT_client_key_exchange")

    /// <summary>
    /// EXPERIMENTAL TLS 1.3 Send DHE FClientKeyShare to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="nsc"> Next security context being negociated </param>
    /// <param name="fp"> Optional fragmentation policy at the record level </param>
    /// <returns> Updated state * Updated next security context * FClientKeyShare message record </returns>
    static member send (st:state, ?nsc:nextSecurityContext, ?fp:fragmentationPolicy) : state * nextSecurityContext * FClientKeyShare =
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in
        let nsc = defaultArg nsc FlexConstants.nullNextSecurityContext in

        let kex13ify (e:kex) : tls13kex = 
            match e with
            | DH13(kex13) -> DHE(kex13.group,kex13.gx)
            | _ -> failwith (perror __SOURCE_FILE__ __LINE__  "invalid KEX for TLS 1.3")
        in
        let kex13l = List.map kex13ify nsc.offers in
        let st,kexl,fcks = FlexClientKeyShare.send(st,kex13l,fp) in
        //BB : Maybe this function should be put somewhere else 
        let choose (uo:kex) (fo:kex) : kex =
            let ukex13 = 
                match uo with
                | DH13(kex13) -> kex13
                | _ -> failwith (perror __SOURCE_FILE__ __LINE__  "invalid KEX for TLS 1.3")
            in
            let fokex13 = 
                match fo with
                | DH13(kex13) -> kex13
                | _ -> failwith (perror __SOURCE_FILE__ __LINE__  "invalid KEX for TLS 1.3")
            in
            let x,gx =
                if ukex13.gx = empty_bytes then fokex13.x,fokex13.gx else ukex13.x,ukex13.gx
            in
            //Sanity check
            if not (ukex13.group = fokex13.group) then
                failwith (perror __SOURCE_FILE__ __LINE__  "Should never happen")
            else
            DH13({group = ukex13.group; x = x; gx = gx; gy = empty_bytes })
        in
        let offers = List.map2 choose nsc.offers kexl in
        let nsc = {nsc with offers = offers } in
        st,nsc,fcks

    /// <summary>
    /// EXPERIMENTAL TLS 1.3 Send DHE FClientKeyShare to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="kexl"> Key Exchange record containing necessary Diffie-Hellman parameters </param>
    /// <param name="fp"> Optional fragmentation policy at the record level </param>
    /// <returns> Updated state * Key Exchange offer list * FClientKeyShare message record </returns>
    static member send (st:state, kex13l:list<tls13kex>, ?fp:fragmentationPolicy) : state * list<kex> * FClientKeyShare =
        LogManager.GetLogger("file").Info("# CLIENT KEY SHARE : FClientKeyShare.send");
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in

        let sampleDH kex =
            match kex with
            | DHE(g,gx) ->
                if gx = empty_bytes then
                    let x,gx = CoreDH.gen_key (dhgroup_to_dhparams g) in
                    x,DHE(g,gx)
                else
                    empty_bytes, kex
        in
        let kex13l = List.map sampleDH kex13l in
        let _,pubkex = List.unzip kex13l in

        let payload = HandshakeMessages.tls13CKEOffersBytes pubkex in
        let st = FlexHandshake.send(st,payload,fp) in

        let fcks = { offers = pubkex ; payload = payload } in
        let kexify e =
            match e with
            | sec,DHE(group,gx) ->
                let kex13 = {group = group; x = sec; gx = gx; gy = empty_bytes } in
                LogManager.GetLogger("file").Debug(sprintf "--- Public Group : %A" group);
                LogManager.GetLogger("file").Debug(sprintf "--- Public Exponent : %s" (Bytes.hexString(gx)));
                LogManager.GetLogger("file").Debug(sprintf "--- Secret Value : %s" (Bytes.hexString(sec)));
                DH13(kex13)
        in
        let kexl = List.map kexify kex13l in
        st,kexl,fcks

    end