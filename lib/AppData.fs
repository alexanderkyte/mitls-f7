﻿module AppData

open Data
open Record
open Error_handling
open TLSInfo
open TLSPlain
open Formats

type pre_app_state = {
  app_info: SessionInfo
  role: Direction
  app_incoming: appdata (* unsolicited data *)
  app_outgoing: appdata
}

type app_state = pre_app_state

let init sinfo role =
    {app_info = sinfo;
     role = role;
     app_outgoing = empty_appdata;
     app_incoming = empty_appdata;}

let reset_incoming app_state =
    {app_state with app_incoming = empty_appdata}

let reset_outgoing app_state =
    {app_state with app_outgoing = empty_appdata}

let set_SessionInfo app_state sinfo =
    {app_state with app_info = sinfo}

let send_data (state:app_state) (data:bytes) =
    (* TODO: different strategies are possible.
        - Append given data to already committed appdata, and re-schedule lengths
        - Ensure the current appdata is empty before committing to the new one,
           otherwise unexpectedError (and refinement types ensure this never happens)
       Currently we implement the latter *)
    if is_empty_appdata state.app_outgoing then
        let lengths = estimateLengths state.app_info (Bytearray.length data) in
        let new_out = appdata state.app_info lengths data in
        {state with app_outgoing = new_out}
    else
        unexpectedError "[send_data] should be invoked only when previously committed data are over."

let retrieve_data (state:app_state) (len:int) =
    let (f,rem) = stream_read state.app_incoming len in
    let state = {state with app_incoming = rem} in
    let res = (f,state) in
    res

let retrieve_data_available state =
    not (is_empty_appdata state.app_incoming)

let next_fragment state len =
    if is_empty_stream state.app_outgoing then
        None
    else
        let (f,rem) = stream_read state.app_outgoing len in
        let state = {state with app_outgoing = rem}
        let res = (f,state) in
        Some res

let recv_fragment (state:app_state) (fragment:fragment) =
    let new_in = stream_write state.app_incoming fragment in
    {state with app_incoming = new_in}