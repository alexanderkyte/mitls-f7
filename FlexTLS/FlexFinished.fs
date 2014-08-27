﻿#light "off"

module FlexFinished

open Tcp
open Bytes
open Error
open TLSInfo
open TLSConstants
open HandshakeMessages

open FlexTypes
open FlexConstants
open FlexHandshake




type FlexFinished = 
    class

    (* Receive an expected Finished message from the network stream *)
    static member receive (st:state) : state * FFinished = 
        let st,hstype,payload,to_log = FlexHandshake.getHSMessage(st) in
        match hstype with
        | HT_finished  -> 
            if length payload <> 0 then
                failwith "recvFinished : payload has not length zero"
            else
                let ff = {  nullFFinished with
                            verify_data = payload; 
                            payload = to_log;
                            } in
                st,ff
        | _ -> failwith "recvFinished : message type is not HT_finished"

    (* Send Finished message to the network stream *)
    static member send (st:state, ?ff:FFinished, ?fp:fragmentationPolicy) : state * FFinished =
        // TODO : check that ServerHelloDone doesn't update the nextSecurityContext
        let ff = defaultArg ff nullFFinished in
        let fp = defaultArg fp defaultFragmentationPolicy in
        let st = FlexHandshake.send(st,HT_finished,ff.verify_data,fp) in
        // TODO : Check if verify_data = payload and cleanup
        let ff = { nullFFinished with
                   verify_data = ff.verify_data;
                   payload = ff.verify_data
                 } in
        st,ff

    end
    