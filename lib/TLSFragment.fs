﻿module TLSFragment

open Error
open Bytes
open TLSInfo
open Formats
open CipherSuites
open DataStream
open StatefulPlain

type datastreams = {
  handshake: stream // Handshake.stream;
  alert: stream // Alert.stream;
  ccs: stream // Handshake.stream;
  appdata: stream // AppDataStream.stream;
}

type prehistory = {
  state : StatefulPlain.history;
  streams: datastreams}

type history = prehistory

type fragment =
    | FHandshake of Fragment.fragment // Handshake.fragment
    | FCCS of Fragment.fragment // Handshake.ccsFragment
    | FAlert of Fragment.fragment // Alert.fragment
    | FAppData of Fragment.fragment // AppDataStream.fragment

let emptyHistory ki =
    let eh = init ki in
    let sh = StatefulPlain.emptyHistory ki in
    let str = 
      { handshake = eh;
        alert = eh;
        ccs = eh;
        appdata = eh} in
      {state = sh; streams = str}

let addToHistory (ki:epoch) ct ss r f =
  match (ct,f) with
    | Handshake,FHandshake(ff) -> 
        let d,s' = Fragment.delta ki ss.streams.handshake r ff in
        let str' = {ss.streams with handshake = s'} in
          {ss with streams = str'}
    | Alert,FAlert(ff) -> 
        let d,s' = Fragment.delta ki ss.streams.alert r ff in
        let str' = {ss.streams with alert = s'} in
          {ss with streams = str'}
          
    | Change_cipher_spec,FCCS(ff) -> 
        let d,s' = Fragment.delta ki ss.streams.ccs r ff in
        let str' = {ss.streams  with ccs = s'} in
          {ss with streams = str'}
    | Application_data,FAppData(ff) -> 
        let d,s' = Fragment.delta ki ss.streams.appdata r ff in
        let str' = {ss.streams with appdata = s'} in
          {ss with streams = str'}
    | _,_ -> unexpectedError "[addToStreams] Incompatible content and fragment types"

let makeAD ki ct =
    let si = epochSI(ki) in
    let pv = si.protocol_version in
    let bct  = ctBytes ct in
    let bver = versionBytes pv in
    if pv = SSL_3p0 
    then bct
    else bct @| bver

let fragmentPlain ki (ct:ContentType) (h:history) (rg:DataStream.range) b = 
    match ct with
    | Handshake ->          FHandshake(Fragment.fragmentPlain ki rg b)
    | Change_cipher_spec -> FCCS(Fragment.fragmentPlain ki rg b)
    | Alert ->              FAlert(Fragment.fragmentPlain ki rg b)
    | Application_data ->   FAppData(Fragment.fragmentPlain ki rg b)


let fragmentRepr ki (ct:ContentType) (h:history) (rg:DataStream.range) frag =
    match frag with
    | FHandshake(f) -> Fragment.fragmentRepr ki rg f
    | FCCS(f) -> Fragment.fragmentRepr ki rg f
    | FAlert(f) -> Fragment.fragmentRepr ki rg f
    | FAppData(f) -> Fragment.fragmentRepr ki rg f


let contents (ki:epoch) (ct:ContentType) (h:history) (rg:range) f =
    match f with
        | FHandshake(f) -> f
        | FCCS(f) -> f
        | FAlert(f) -> f
        | FAppData(f) -> f
            

let construct (ki:epoch) (ct:ContentType) (h:history) (rg:range) sb =
    match ct with
        | Handshake -> FHandshake(sb)
        | Change_cipher_spec -> FCCS(sb)
        | Alert -> FAlert(sb)
        | Application_data -> FAppData(sb)

let TLSFragmentToFragment ki ct ss h rg f =
    let sb = contents ki ct ss rg f in
    StatefulPlain.construct ki h (makeAD ki ct) rg sb

let fragmentToTLSFragment ki ct ss h rg f =
    let sb = StatefulPlain.contents ki h (makeAD ki ct) rg f in
    construct ki ct ss rg sb
