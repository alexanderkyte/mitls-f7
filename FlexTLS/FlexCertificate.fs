﻿#light "off"

module FlexCertificate

open Tcp
open Bytes
open Error
open TLSInfo
open HandshakeMessages

open FlexTypes
open FlexFragment


(* TODO : Find another way to emmbed Role for FSessionInfo update *)

type FlexCertificate = 
    class

    (* Receive a Certificate message from the network stream *)
    static member receive (role:Role) (st:state) (si:SessionInfo) : state * SessionInfo * FCertificate =
        
        let st,hstype,payload,to_log = FlexFragment.getHSMessage(st) in
        match hstype with
        | HT_certificate  ->  
            (match parseClientOrServerCertificate payload with
            | Error (ad,x) -> failwith x
            | Correct (certC) -> 
                let cert = { nullFCertificate with chain = certC; } in
                match role with
                | Client ->
                    let si  = { si with 
                                client_auth = true;
                                clientID = certC;
                    } in
                    (st,si,cert)
                | Server ->
                    let si  = { si with 
                                serverID = certC;
                    } in
                    (st,si,cert)
            )
        | _ -> failwith "recvCertificate : message type should be HT_certificate"

    (* Send a Certificate message to the network stream *)

    end
