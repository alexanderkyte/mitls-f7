﻿(* Copyright (C) 2012--2014 Microsoft Research and INRIA *)

#light "off"

module DHGroup

open Bytes
open Error
open TLSError

type elt = bytes

#if ideal
type preds = | Elt of bytes * bytes * elt
type predPP = | PP of bytes * bytes

let goodPP_log = ref([]: list<CoreKeys.dhparams>)
#if verify
let goodPP (dhp:CoreKeys.dhparams) : bool = failwith "only used in ideal implementation, unverified"
#else
let goodPP dhp =  List.memr !goodPP_log dhp
#endif

let pp (dhp:CoreKeys.dhparams) : CoreKeys.dhparams =
#if verify
    Pi.assume(PP(dhp.dhp,dhp.dhg));
#else
    goodPP_log := (dhp ::!goodPP_log);
#endif
    dhp
#endif



let genElement dhp: elt =
    let (_, e) = CoreDH.gen_key dhp in
#if verify
    Pi.assume (Elt(dhp.dhp,dhp.dhg,e));
#endif
    e

let checkParams dhdb minSize p g =
    match CoreDH.check_params dhdb DHDBManager.defaultDHPrimeConfidence minSize p g with
    | Error(x) -> Error(AD_insufficient_security,x)
    | Correct(res) ->
        let (dhdb,dhp) = res in
#if ideal
        let dhp = pp(dhp) in
        let rp = dhp.dhp in
        let rg = dhp.dhg in
        if rp <> p || rg <> g then
            failwith "Trusted code returned inconsitent value"
        else
#endif
        correct (dhdb,dhp)

let checkElement dhp (b:bytes): option<elt> =
    if CoreDH.check_element dhp b then
        (
#if verify
        Pi.assume(Elt(dhp.dhp,dhp.dhg,b));
#endif
        Some(b))
    else
        None

let defaultDHparams file dhdb minSize =
    let (dhdb,dhp) = DHDBManager.load_default_params file dhdb minSize in
#if ideal
    let dhp = pp(dhp) in
#endif
    (dhdb,dhp)