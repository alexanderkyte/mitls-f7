﻿#light "off"

module FlexTLS.FlexServerHelloDone

open NLog

open Bytes
open Error
open HandshakeMessages

open FlexTypes
open FlexConstants
open FlexHandshake




/// <summary>
/// Module receiving, sending and forwarding TLS Server Hello Done messages.
/// </summary>
type FlexServerHelloDone = 
    class

    /// <summary>
    /// Receive a ServerHelloDone message from the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <returns> Updated state * FServerHelloDone message record </returns>
    static member receive (st:state) : state * FServerHelloDone =
        LogManager.GetLogger("file").Info("# SERVER HELLO DONE : FlexServerHelloDone.receive");
        let st,hstype,payload,to_log = FlexHandshake.receive(st) in
        match hstype with
        | HT_server_hello_done  -> 
            if length payload <> 0 then
                failwith (perror __SOURCE_FILE__ __LINE__ "payload has not length zero")
            else
                let fshd: FServerHelloDone = {payload = to_log} in
                LogManager.GetLogger("file").Info(sprintf "--- Payload : %s" (Bytes.hexString(payload)));
                st,fshd
        | _ -> failwith (perror __SOURCE_FILE__ __LINE__ (sprintf "Unexpected handshake type: %A" hstype))

    /// <summary>
    /// Prepare ServerHelloDone message bytes that will not be sent to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <returns> FServerHelloDone message bytes * Updated state * FServerHelloDone message record </returns>
    static member prepare () : FServerHelloDone =
        let payload = HandshakeMessages.serverHelloDoneBytes in
        let fshd: FServerHelloDone = { payload = payload } in
        fshd

    /// <summary>
    /// Send a ServerHelloDone message to the network stream
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="fp"> Optional fragmentation policy at the record level </param>
    /// <returns> Updated state * FServerHelloDone message record </returns>
    static member send (st:state, ?fp:fragmentationPolicy) : state * FServerHelloDone =
        LogManager.GetLogger("file").Info("# SERVER HELLO DONE : FlexServerHelloDone.send");
        let fp = defaultArg fp FlexConstants.defaultFragmentationPolicy in
        
        let fshd = FlexServerHelloDone.prepare() in
        let st = FlexHandshake.send(st,fshd.payload,fp) in
        st,fshd

    end