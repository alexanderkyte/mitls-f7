﻿module Error

type ('a,'b) OptResult =
    | Error of 'a
    | Correct of 'b

let perror (file:string) (line:string) (text:string) =
#if verify
    text
#else
    Printf.sprintf "Error at %s:%s: %s." file line (if text="" then "No reason given" else text)
#endif

let correct x = Correct x

let unexpected info = failwith info
let unreachable info = failwith info