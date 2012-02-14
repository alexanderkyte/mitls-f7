﻿module TLSFragment

open Error
open Bytes
open TLSInfo
open Formats
open CipherSuites

type fragment =
    | FHandshake of Handshake.fragment
    | FCCS of Handshake.ccsFragment
    | FAlert of Alert.fragment
    | FAppData of AppDataStream.fragment

let TLSFragmentRepr ki (tlen:DataStream.range) seqn (ct:ContentType) frag =
    match frag with
    | FHandshake(f) -> Handshake.repr ki 0 seqn f
    | FCCS(f) -> Handshake.ccsRepr ki 0 seqn f
    | FAlert(f) -> Alert.repr ki 0 seqn f
    | FAppData(f) -> AppDataStream.repr ki 0 seqn f

let TLSFragment ki (tlen:DataStream.range) seqn (ct:ContentType) b =
    match ct with
    | Handshake ->          FHandshake(Handshake.fragment ki 0 seqn b)
    | Change_cipher_spec -> FCCS(Handshake.ccsFragment ki 0 seqn b)
    | Alert ->              FAlert(Alert.fragment ki 0 seqn b)
    | Application_data ->   FAppData(AppDataStream.fragment ki 0 seqn b)

type addData = bytes

let makeAD pv seqn ct =
    let bseq = bytes_of_seq seqn in
    let bct  = ctBytes ct in
    let bver = versionBytes pv in
    if pv = SSL_3p0 
    then bseq @| bct
    else bseq @| (bct @| bver)

let parseAD pv ad =
    if pv = SSL_3p0 then
        let (seq8,ct1) = split ad 8 in
        let seqn = seq_of_bytes seq8 in
        match parseCT ct1 with
        | Error(x,y) -> unexpectedError "[parseAD] should always be invoked on valid additional data"
        | Correct(ct) -> (seqn,ct)
    else
        let (seq8,rem) = split ad 8 in
        let (ct1,bver) = split rem 1 in
        if bver <> versionBytes pv then
          unexpectedError "[parseAD] should always be invoked on valid additional data"
        else
          let seqn = seq_of_bytes seq8 in
            match parseCT ct1 with
              | Error(x,y) -> unexpectedError "[parseAD] should always be invoked on valid additional data"
              | Correct(ct) -> (seqn,ct)

type AEADPlain = fragment
type AEADMsg = fragment
let AEADPlain (ki:KeyInfo) (tlen:DataStream.range) (ad:addData) (b:bytes) = 
  let (seq,ct) = parseAD ki.sinfo.protocol_version ad in
  TLSFragment ki tlen seq ct b

let AEADRepr (ki:KeyInfo) (tlen:DataStream.range) (ad:addData) (f:fragment) = 
  let (seq,ct) = parseAD ki.sinfo.protocol_version ad in
    TLSFragmentRepr ki tlen seq ct f


let AEADPlainToTLSFragment (ki:KeyInfo) (i:DataStream.range) (ad:addData) (aead:AEADPlain) = aead

let TLSFragmentToAEADPlain (ki:KeyInfo) (i:DataStream.range) (seqn:int) (ct:ContentType) (disp:fragment) = disp

