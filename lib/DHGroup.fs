module DHGroup

open Bytes

type p   = bytes
type elt = bytes 
type g   = elt

let private dhparams p g: CoreKeys.dhparams = { p = p; g = g }

let genElement p g: elt =
    let (_, (e, _)) = CoreDH.gen_key (dhparams (cbytes p) (cbytes g)) in
    abytes e

let checkElement (p:p) (b:bytes) :elt option =
    if CoreDH.check_element (cbytes p) (cbytes b) then
        Some(b)
    else
        None