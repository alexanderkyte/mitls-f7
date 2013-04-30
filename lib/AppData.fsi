module AppData

open TLSInfo
open Bytes
open Error
open TLSError
open DataStream
open Range

type app_state

val inStream:  ConnectionInfo -> app_state -> stream
val outStream: ConnectionInfo -> app_state -> stream

val init: ConnectionInfo -> app_state

val writeAppData: ConnectionInfo -> app_state -> range -> AppFragment.fragment -> stream -> app_state

val next_fragment: ConnectionInfo -> app_state -> (range * AppFragment.fragment * app_state) option

val clearOutBuf: ConnectionInfo -> app_state -> app_state

val recv_fragment: ConnectionInfo ->  app_state -> range -> AppFragment.fragment -> app_state

val readAppData: ConnectionInfo -> app_state -> ((range * delta) option * app_state)

val reset_incoming:  ConnectionInfo -> app_state -> ConnectionInfo -> app_state

val reset_outgoing:  ConnectionInfo -> app_state -> ConnectionInfo -> app_state
