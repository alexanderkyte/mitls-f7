﻿module MACPlain

open Bytes
open Algorithms
open CipherSuites
open TLSInfo

type MACPlain = {p:bytes}

let MACPlain (ki:KeyInfo) (rg:DataStream.range) ad f =
    let fB = AEADPlain.repr ki rg ad f
    let fLen = bytes_of_int 2 (length fB) in
    let fullData = ad @| fLen in 
    {p = fullData @| fB}

let reprMACPlain (ki:KeyInfo) (tlen:DataStream.range) p = p.p

type MACed = {m:bytes}
let MACed (ki:KeyInfo) (tlen:DataStream.range) b = {m=b}
let reprMACed (ki:KeyInfo) (tlen:DataStream.range) m = m.m


let parseNoPad ki tlen ad plain =
  let min,max = tlen in
    // assert length plain = tlen
    let cs = ki.sinfo.cipher_suite in
    let maclen = macSize (macAlg_of_ciphersuite cs) in
    let macStart = min - maclen
    if macStart < 0 || length(plain) < macStart then
        (* FIXME: is this safe?
           I (AP) think so because our locally computed mac will have some different length.
           Also timing is not an issue, because the attacker can guess the check should fail anyway. *)
    //CF: no, the MAC has the wrong size; I'd rather have a static precondition on the length of c.
        let aeadF = AEADPlain.plain ki tlen ad plain
        let tag = MACed ki tlen [||]
        (aeadF,tag)
    else
        let (frag,mac) = split plain macStart in
        let aeadF = AEADPlain.plain ki tlen ad frag
        let tag = MACed ki tlen mac
        (aeadF,tag)
