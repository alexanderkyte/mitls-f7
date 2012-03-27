﻿module AEAD

open Bytes
open CipherSuites
open Algorithms
open TLSInfo
open Error

type cipher = bytes

type AEADKey =
    | MtE of MAC.key * ENC.state
    | MACOnly of MAC.key
(*  |   GCM of AENC.state  *)

let GEN ki =
    let cs = ki.sinfo.cipher_suite in
    match cs with
    | x when isOnlyMACCipherSuite x ->
        let mk = MAC.GEN ki
        (MACOnly(mk), MACOnly(mk))
    | _ ->
        let mk = MAC.GEN ki in
        let (ek,dk) = ENC.GEN ki in
        (MtE(mk,ek),MtE(mk,dk))

let COERCE ki b =
    // precondition: b is of the right length. No runtime checks here.
    let cs = ki.sinfo.cipher_suite in
    match cs with
    | x when isOnlyMACCipherSuite x ->
        let mk = MAC.COERCE ki b in
        MACOnly(mk)
    | _ ->
        let macKeySize = macKeySize (macAlg_of_ciphersuite cs) in
        let encKeySize = encKeySize (encAlg_of_ciphersuite cs) in
        let ivsize = 
            if PVRequiresExplicitIV ki.sinfo.protocol_version then 0
            else ivSize (encAlg_of_ciphersuite ki.sinfo.cipher_suite)
        let mkb = Array.sub b 0 macKeySize in
        let ekb = Array.sub b macKeySize encKeySize in
        let ivb = Array.sub b (macKeySize+encKeySize) ivsize in
        let mk = MAC.COERCE ki mkb in
        let ek = ENC.COERCE ki ekb ivb in
        MtE(mk,ek)

let LEAK ki k =
    match k with
    | MACOnly(mk) -> MAC.LEAK ki mk
    | MtE(mk,ek) ->
        let (k,iv) = ENC.LEAK ki ek in
        MAC.LEAK ki mk @| k @| iv

let encrypt ki key data rg plain =
    match key with
    | MtE (ka,ke) ->
        let maced   = AEPlain.concat ki rg data plain
        let tag     = AEPlain.mac    ki ka maced  
        let (tlen,encoded) = AEPlain.encode ki rg data plain tag
        let (ke,res) = ENC.ENC ki ke tlen encoded in
        (MtE(ka,ke),res)
    | MACOnly (ka) ->
        let maced   = AEPlain.concat ki rg data plain
        let tag     = AEPlain.mac    ki ka maced  
        let (tlen,encoded) = AEPlain.encodeNoPad ki rg data plain tag
        (key,AEPlain.repr ki tlen encoded)

//  | auth only -> ...
//  | GCM (GCMKey) -> ... 
        
let decrypt ki key data cipher =
    match key with
    | MtE (ka,ke) ->
        let (ke,encoded)         = ENC.DEC ki ke cipher in
        let (rg,plain,tag,decodeOk) = AEPlain.decode ki data (length cipher) encoded in
        let maced                   = AEPlain.concat ki rg data plain 
        match ki.sinfo.protocol_version with
        | SSL_3p0 | TLS_1p0 ->
            if decodeOk
            then 
                if AEPlain.verify ki ka maced tag (* padding time oracle *) 
                then correct(MtE(ka,ke),rg,plain)
                else Error(MAC,CheckFailed)
            else     Error(RecordPadding,CheckFailed) (* padding error oracle *)
        | TLS_1p1 | TLS_1p2 ->
            if AEPlain.verify ki ka maced tag 
            then 
                if decodeOk then correct (MtE(ka,ke),rg,plain)                
                else Error(MAC,CheckFailed)
            else     Error(MAC,CheckFailed)
    | MACOnly (ka) ->
        let encoded = AEPlain.plain ki (length cipher) cipher in
        let (rg,plain,tag) = AEPlain.decodeNoPad ki data (length cipher) encoded in
        let maced          = AEPlain.concat ki rg data plain
        if AEPlain.verify ki ka maced tag then
            correct (key,rg,plain)
        else
            Error(MAC,CheckFailed)
//  | GCM (GCMKey) -> ... 

(*
let encrypt ki key iv3 tlen data plain =
    match key with
    | MtE (macKey,encKey) ->
        //CF no, we need some TLSPlain.MAC. And encrypt cannot fail. 
        let text = MACPlain.MACPlain ki tlen data plain in
        let mac = MAC.MAC {ki=ki;tlen=tlen} macKey text in
        let toEncrypt = Plain.prepare ki tlen data plain mac in
        ENC.ENC ki encKey iv3 tlen toEncrypt

let decrypt ki key iv tlen ad cipher =
    match key with
    | MtE (macKey, encKey) ->
        let (iv3,compr_and_mac_and_pad) = ENC.DEC ki encKey iv cipher in
        let (mustFail,(compr,mac)) = Plain.parse ki tlen ad compr_and_mac_and_pad in
        let toVerify = MACPlain.MACPlain ki tlen ad compr in
        (* If mustFail is true, it means some padding error occurred.
            If in early versions of TLS, insecurely report a padding error now *)
        match ki.sinfo.protocol_version with
        | SSL_3p0 | TLS_1p0 ->
            if mustFail then
                Error(RecordPadding,CheckFailed)
            else
                if MAC.VERIFY {ki=ki;tlen=tlen} macKey toVerify mac then
                    correct(iv3,compr)
                else
                    Error(MAC,CheckFailed)
        | TLS_1p1 | TLS_1p2 ->
            if MAC.VERIFY {ki=ki;tlen=tlen} macKey toVerify mac then
                if mustFail then
                    Error(MAC,CheckFailed)
                else
                    correct (iv3,compr)
            else
                Error(MAC,CheckFailed)
*)
