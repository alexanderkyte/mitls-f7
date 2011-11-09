﻿(* Application data protocol *)

(* We do not support warnings, as there is no good reason to do so *)

module AppData

open Data
open Record
open Error_handling
open TLSInfo
open TLSPlain

type pre_app_state
type app_state = pre_app_state

val init: SessionInfo -> Direction -> app_state

val reset_incoming: app_state -> app_state
val reset_outgoing: app_state -> app_state
val set_SessionInfo: app_state -> SessionInfo -> app_state

(* Application data to/form application *)

(* Enqueue app data in the output buffer *)
val send_data: app_state -> bytes -> app_state

(* Dequeue app data from the input buffer *)
val retrieve_data: app_state -> int -> (bytes * app_state)
val retrieve_data_available: app_state -> bool

(* Application data to/from dispatcher (hence record) *)

(* Dequeue app data from the output buffer *)
val next_fragment: app_state -> ((int * fragment) * app_state) option
(* Enqueue app data in the input buffer, only called on an empty input buffer *)
val recv_fragment: app_state -> fragment -> app_state
