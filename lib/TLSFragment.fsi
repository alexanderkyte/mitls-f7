﻿module TLSFragment

open Bytes
open TLSInfo
open Formats
open CipherSuites
open DataStream

type datastreams
type prehistory
type history = prehistory

type fragment =
    | FHandshake of Fragment.fragment
    | FCCS of Fragment.fragment
    | FAlert of Fragment.fragment
    | FAppData of Fragment.fragment

val emptyHistory: epoch -> history
val addToHistory: epoch -> ContentType -> history -> range -> StatefulPlain.fragment -> history

val makeAD: epoch -> ContentType -> StatefulPlain.data 
val fragmentPlain: epoch -> ContentType -> history -> range -> bytes -> fragment
val fragmentRepr:     epoch -> ContentType -> history -> range -> fragment -> bytes


val contents:  epoch -> ContentType -> history -> range -> fragment -> Fragment.fragment
val construct: epoch -> ContentType -> history -> range -> Fragment.fragment -> fragment

val TLSFragmentToFragment: epoch -> ContentType -> history -> range -> fragment -> StatefulPlain.fragment
val fragmentToTLSFragment: epoch -> ContentType -> history -> range -> StatefulPlain.fragment -> fragment
