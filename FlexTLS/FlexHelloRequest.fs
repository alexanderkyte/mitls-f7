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
        let st,hstype,payload,to_log = FlexHandshake.receive(st) in
        match hstype with
        | HT_hello_request  ->         
            if length payload <> 0 then
                failwith (perror __SOURCE_FILE__ __LINE__ "payload has not length zero")
            else
                let fhr = {FlexConstants.nullFHelloRequest with payload = to_log} in
                LogManager.GetLogger("file").Info(sprintf "--- Payload : %s" (Bytes.hexString(payload)));
                st,fhr
        | _ -> failwith (perror __SOURCE_FILE__ __LINE__ (sprintf "Unexpected handshake type: %A" hstype))


    /// <summary>
    /// Prepare a HelloRequest message that will not be sent
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <returns> FHelloRequest message bytes * Updated state * next security context * FHelloRequest message record </returns>
    static member prepare () : FHelloRequest =
        let payload = HandshakeMessages.messageBytes HT_hello_request empty_bytes in
        let fhr : FHelloRequest = {payload = payload} in
        fhr

    /// <summary>
    /// Send a HelloRequest message to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="fp"> Optional fragmentation policy applied to the message </param>
    /// <returns> Updated state * next security context * FHelloRequest message record </returns>
    static member send (st:state, ?fp:fragmentationPolicy) : state * FHelloRequest =
        LogManager.GetLogger("file").Info("# HELLO REQUEST : FlexHelloRequest.send");
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in

        let fhr = FlexHelloRequest.prepare() in
        let st = FlexHandshake.send(st,fhr.payload,fp) in
        st,fhr

    end
