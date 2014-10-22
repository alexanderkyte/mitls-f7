﻿#light "off"

module FlexApps.Application

open NLog

open FlexTLS
open FlexTypes

open FlexApps
open Attack_Alert
open Attack_FragmentClientHello
open Attack_EarlyCCS
open Attack_TripleHandshake
open Handshake_full_RSA
open Handshake_full_DHE
open Handshake_resumption
open Handshake_tls13
open Metrics_DHE



[<EntryPoint>]
let main argv = 
    
    (* Log facility*)
    let log = LogManager.GetLogger("file") in
    log.Info("START Running FlexTLS scenario");

    (* OpenSSL tests *)
//    OpenSSL_tests.opensslTest 2443 "127.0.0.1" 2444;

    (* Alert attack *)
//    let st = Attack_Alert.run "www.google.com" in

    (* Alert attack MITM *)
//    let sst,cst = Attack_Alert.runMITM("0.0.0.0","127.0.0.1",4433) in

    (* Protocol downgrade attack (Fragmented ClientHello) *)
//    let st = Attack_FragmentClientHello.run("www.inria.fr",fp=All(5)) in
//    printf "Protocol version downgrade attack finished\n";

    (* Protocol downgrade attack MITM (Fragmented ClientHello) *)
//    let sst,cst = Attack_FragmentClientHello.runMITM("0.0.0.0","127.0.0.1",4433) in
//    printf "Protocol version downgrade attack finished\n";

    (* Early CCS attack MITM *)
//    let sst,cst = Attack_EarlyCCS.runMITM("0.0.0.0","127.0.0.1",4433) in
//    printf "Early CCS attack finished\n";

    (* Triple handshake attack MITM *)
//    let sst,cst = Attack_TripleHandshake.runMITM("0.0.0.0","rsa.cert-01.mitls.org",6666,"127.0.0.1",4433) in
//    printf "Triple handshake attack finished\n";

    (* Experimental TLS 1.3 full handshake as Client *)
//    printf "Starting TLS 1.3 client\n";
//    let st = Handshake_tls13.client("127.0.0.1","rsa.cert-01.mitls.org",4433) in
//    printf "TLS 1.3 client finished\n";

    (* Experimental TLS 1.3 full handshake as Server *)
//    printf "Starting TLS 1.3 server\n";
//    let st = Handshake_tls13.server("0.0.0.0","rsa.cert-01.mitls.org",4433) in
//    printf "TLS 1.3 server finished\n";

    (* Standard RSA full handshake as Client *)
//    let st = Handshake_full_RSA.client("www.inria.fr") in
//    printf "RSA client finished\n";

    (* Standard RSA handshake with resumption as Client*)
//    let st = Handshake_full_RSA.client("www.inria.fr") in
//    let _  = Tcp.close st.ns in 
//    let st = Handshake_resumption.client(st,"www.inria.fr") in
//    printf "RSA client resumption finished\n";

    (* Standard RSA full handshake with client authentication as Client *)
//    let st = Handshake_full_RSA.client_with_auth("127.0.0.1","rsa.cert-01.mitls.org",44101) in
//    printf "RSA client_auth finished\n";
    
    (* Standard DHE full handshake as Client *)
//    let st = Handshake_full_DHE.client("www.inria.fr") in
//    printf "DHE client finished\n";

    (* Standard DHE full handshake with client authentication as Client *)
//    let st = Handshake_full_DHE.client_with_auth("127.0.0.1","rsa.cert-01.mitls.org",44102) in
//    printf "DHE client_auth finished\n";

    (* Standard RSA full handshake as Server *)
//    printf "Running RSA server. Please connect to port 44201\n";
//    let st = Handshake_full_RSA.server("0.0.0.0","rsa.cert-01.mitls.org",44201) in
//    printf "RSA server finished\n";

    (* Standard RSA full handshake with client authentication as Server *)
//    printf "Running RSA server. Please connect to port 44202\n";
//    let st = Handshake_full_RSA.server_with_client_auth("0.0.0.0","rsa.cert-01.mitls.org",44202) in
//    printf "RSA server_with_client_auth finished\n";

    (* Standard DHE full handshake as Server *)
//    printf "Running RSA server. Please connect to port 44203\n";
//    let st = Handshake_full_DHE.server("127.0.0.1","rsa.cert-01.mitls.org",44203) in
//    printf "DHE server finished\n";

    (* Standard DHE full handshake with client authentication as Server *)
//    printf "Running RSA server. Please connect to port 44204\n";
//    let st = Handshake_full_DHE.server_with_client_auth("127.0.0.1","rsa.cert-01.mitls.org",44204) in
//    printf "DHE server_with_client_auth finished\n";

    (* Metrics for DH parameters of ServerKeyExchange *)
//    printf "Running Metrics for DH parameters\n";
//    Metrics_DHE.run_multi("list.data");

    log.Info("STOP Running FlexTLS scenario");
    ignore (System.Console.ReadLine());
    0
