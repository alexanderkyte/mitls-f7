﻿#light "off"

module FlexTLS.FlexHelloRequest

open NLog

open Bytes
open Error
open HandshakeMessages

open FlexTypes
open FlexConstants
open FlexHandshake




/// <summary>
/// Module receiving, sending and forwarding TLS Hello Request messages.
/// </summary>
type FlexHelloRequest = 
    class

    /// <summary>
    /// Receive a HelloRequest message from the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <returns> Updated state * FHelloRequest message record </returns>
    static member receive (st:state) : state * FHelloRequest = 
        LogManager.GetLogger("file").Info("# HELLO REQUEST : FlexHelloRequest.receive");
        let st,hstype,payload,to_log = FlexHandshake.getHSMessage(st) in
        match hstype with
        | HT_hello_request  ->         
            if length payload <> 0 then
                failwith (perror __SOURCE_FILE__ __LINE__ "payload has not length zero")
            else
                let fhr = {FlexConstants.nullFHelloRequest with payload = to_log} in
                LogManager.GetLogger("file").Info(sprintf "--- Payload : %s" (Bytes.hexString(payload)));
                st,fhr
        | _ -> failwith (perror __SOURCE_FILE__ __LINE__ "message is not of type HelloRequest")


    /// <summary>
    /// Prepare a HelloRequest message that will not be sent
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <returns> FHelloRequest message bytes * Updated state * next security context * FHelloRequest message record </returns>
    static member prepare (st:state) : bytes * state * FHelloRequest =
        let payload = HandshakeMessages.messageBytes HT_hello_request empty_bytes in
        payload,st,{payload = payload}

    /// <summary>
    /// Send a HelloRequest message to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="fp"> Optional fragmentation policy applied to the message </param>
    /// <returns> Updated state * next security context * FHelloRequest message record </returns>
    static member send (st:state, ?fp:fragmentationPolicy) : state * FHelloRequest =
        LogManager.GetLogger("file").Info("# HELLO REQUEST : FlexHelloRequest.send");
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in
        let ns = st.ns in
        let payload = HandshakeMessages.messageBytes HT_hello_request empty_bytes in
        let st = FlexHandshake.send(st,payload,fp) in
        LogManager.GetLogger("file").Info(sprintf "--- Payload : %s" (Bytes.hexString(payload)));
        st,{payload = payload}

    end
