module LHAEPlain
open Bytes
open Error
open TLSError
open TLSConstants
open TLSInfo
open Range

type adata = bytes

let makeAD (i:id) ((seqn,h):StatefulPlain.history) ad =
    let bn = bytes_of_seq seqn in
    bn @| ad

// We statically know that ad is big enough
let parseAD (i:id) ad = 
    let (snb,ad) = Bytes.split ad 8 in
    ad

type fragment = {contents:StatefulPlain.fragment}
type plain = fragment

let plain (i:id) (ad:adata) (rg:range) b =
    let ad = parseAD i ad in
    let h = StatefulPlain.emptyHistory i in
    match StatefulPlain.plain i h ad rg b with
    | Error(x,y) -> Error(x,y)
    | Correct(p) -> correct ({contents =  p})

let reprFragment (i:id) (ad:adata) (rg:range) p =
    let ad = parseAD i ad in
    StatefulPlain.reprFragment i ad rg p.contents

let repr i ad rg p = reprFragment i ad rg p

let StatefulPlainToLHAEPlain (i:id) (h:StatefulPlain.history) 
    (ad:StatefulPlain.adata) (ad':adata) (r:range) f = {contents = f}
let LHAEPlainToStatefulPlain (i:id) (h:StatefulPlain.history) 
    (ad:StatefulPlain.adata) (ad':adata) (r:range) f = f.contents

#if ideal
let widen i ad r f =
    let ad' = parseAD i ad in
    let f' = StatefulPlain.widen i ad' r f.contents in
    {contents = f'}
#endif
