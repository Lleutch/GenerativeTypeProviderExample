﻿module ScribbleGenerativeTypeProvider.AsstScribbleParser

open FSharp.Data
open System
open System.Text.RegularExpressions
open FParsec
open AssertionParsing
open AssertionParsing.FuncGenerator


let nextNumber = 
    let counter = ref 0
    fun () -> 
        counter.Value <- !counter + 1
        !counter

let printListJson (aList:list<string*string>) =
    let length = aList.Length
    List.fold
        (fun (state,index) ((varName, varType):string*string) ->
            (   if index < length then
                    sprintf """%s{"varName":"%s", "varType":"%s" },""" state varName varType
                else
                    sprintf """%s{"varName":"%s", "varType":"%s"}""" state varName varType
             ,index+1)
        ) ("[",1) aList
    |> fun (state,_) -> state + "]"

type Current = Current of int
type Role = Role of string
type Partner = Partner of string
type Label = Label of string
type VarName = VarName of string
type VarType = VarType of string
type TrPayload = TrPayload of List<string*string>
type EventType = EventType of string
type Next = Next of int
type Assertion = Assertion of string

type Transition =
    {
        Current     : Current
        Role        : Role
        Partner     : Partner
        Label       : Label
        TrPayload   : TrPayload    
        Assertion   : Assertion
        EventType   : EventType
        Next        : Next
    }
    member this.Stringify() =
        let (Current current)       = this.Current
        let (Role role)             = this.Role
        let (Partner partner)       = this.Partner
        let (Label label)           = this.Label
        let (TrPayload payload)     = this.TrPayload     
        let (EventType eventType)   = this.EventType
        let (Next next)             = this.Next
        let (Assertion assertion)   = this.Assertion

        sprintf
            """{ "currentState": %i , "localRole":"%s" , "partner":"%s" , "label":"%s" , "payload": %s , "assertion": "%s", "type":"%s" , "nextState":%i  } """        
            current
            role
            partner
            label
            (if payload.Length = 1 && payload.[0] = ("", "") then
                printListJson []
             else
                printListJson payload
            )
            assertion
            eventType
            next         

type StateMachine =
    | Transitions of Transition list
    member this.Stringify() =
        let (Transitions transitionList) = this
        let length = transitionList.Length
        List.fold
            (fun (state,index) (transition:Transition) ->
                (if index < length then
                    state + (transition.Stringify()) + ",\n"
                 else
                    state + (transition.Stringify())                    
                , index + 1)
            ) ("[",1) transitionList
        |> fun (state,_) -> state + "]"

module parserHelper =
    let brackets = ('{','}')
    let squareBrackets = ('[',']')
    let quotes = ('\"','\"')
    let str_ws s = spaces >>. pstring s .>> spaces
    let char_ws c = pchar c .>> spaces
    let anyCharsTill pEnd = manyCharsTill anyChar pEnd
    let anyCharsTillApply pEnd f = manyCharsTillApply anyChar pEnd f
    let quoted quote parser = 
        pchar (quote |> fst) 
        .>> spaces >>. parser .>> spaces 
        .>> pchar (quote |> snd) 
    let line:Parser<string,unit> = anyCharsTill newline
    let restOfLineAfter str = str_ws str >>. line
    let startUpUseless:Parser<_,unit> = 
        pstring "compound = true;" 
        |> anyCharsTill
        >>. skipNewline 
    let isUnderscore c = c='_'                                          
    let isVar c = isLetter c || isDigit c || c='_'
    //let dummy = many1Satisfy2L isUnderscore isVar "identifier" .>> spaces          
    let varParser:Parser<_, unit> = 
        many1Satisfy isVar .>> spaces
    let expr:Parser<_, unit> = 
        let normalChar 
            = satisfy (fun c -> c <> ';' && c<>'\\' && c<>'\"')
        (manyChars normalChar) 
    // pstring "\"" .>> spaces .>> pstring "];" >>. spaces

    let current:Parser<_,unit> = 
        spaces 
        >>. quoted quotes pint32 .>> spaces 
        |>> Current
    let next:Parser<_,unit> = 
        spaces 
        >>. quoted quotes pint32 .>> spaces 
        |>> Next
    
    let partnerEvent:Parser<_,unit> =
        str_ws "label"
        >>. pstring "=" >>. spaces
        >>. pchar '\"'
        >>. (anyCharsTillApply (attempt (pstring "!!" <|> pstring "!" <|> pstring "??" <|> pstring "?")) (fun str event -> (str,event)))
        |>> fun (str,event) -> 
                match event with
                | "!!" -> 
                    Partner(str),EventType("request")
                | "??" -> 
                    Partner(str),EventType("accept")
                | "!" -> 
                    Partner(str),EventType("send")
                | "?" ->
                    Partner(str),EventType("receive")                
                | _ ->
                    failwith "This case can never happen, if these two weren't here the flow would
                    have been broken earlier!!"

    let label:Parser<_,unit> = 
        spaces
        >>. (anyCharsTill (pchar '('))
        |>> Label
    
    let payload:Parser<_,unit> =
        //let varPayload = spaces >>. manyChars (noneOf [',';')';':'])
        //let typePayload = spaces >>. manyChars (noneOf [',';')'])
        //let singlePayload = pipe3 varPayload (pstring ":") (spaces >>. varPayload) (fun id _ str ->  (id, str))
        let varName = manyChars (noneOf [',';')'; ':']) 
        let unitType = ((pstring "_" >>. varName ) <|> (pstring "Unit")) |>> (fun x -> "")
        let dummyVars = (pstring "_" >>. varName ) //|>> (fun x -> "")
        let singlePayload = 
             attempt
                (pipe4 (spaces >>.  varName) (pstring ":") spaces (unitType <|> varName)
                        (fun name _ _ varType -> 
                                if (varType<>"") 
                                        then  (sprintf "%s" name, sprintf "%s" varType) 
                                else sprintf "", ""))
                <|> ((spaces >>. (unitType <|> varName)) |>> 
                     (fun varType -> 
                        if (varType<>"") then  (sprintf "_dummy%i" (nextNumber()), sprintf "%s" varType) 
                        else sprintf "", ""))

        spaces  >>. (sepBy singlePayload (pstring ",")) .>> (pstring ")")
        |>> TrPayload           
    //(pstring ")\"" >>. spaces >>. pstring "];" >>. spaces)    
    let assertion:Parser<_,unit> =
        let endOfPayload = pstring "\"" >>. spaces >>. pstring "];" >>. spaces
        ((endOfPayload |>> fun x -> "") <|> ((pstring "@" >>. (between (pstring "\\\"")  (pstring "\\\"") expr))  .>> endOfPayload)) 
        |>> Assertion

    let transition role currentState =
        parse{
            let! _ = pstring "->"
            let! nextState = next
            let! _ = pstring "["
            let! partner,eventType = partnerEvent
            let! label = label
            let! payload = payload
            let! assertion = assertion
            return 
                {
                    Current     = currentState
                    Role        = Role role
                    Partner     = partner
                    Label       = label
                    TrPayload     = payload
                    Assertion   = assertion      
                    EventType   = eventType
                    Next        = nextState
                } |> Some
        }
    let skipLabelInfoLine:Parser<Transition option,unit> =
         parse{
            let! _ = pstring "[" .>> spaces
            let! _ = manyCharsTill anyChar (pstring "];")
            let! _ = spaces
            return None
        }
    let transitionOrSkipping role =
        parse{
            let! _ = spaces
            let! currentState = current .>> spaces
            return! transition role currentState <|> skipLabelInfoLine
        }
    let transitions role = 
        parse{
            let! _ = startUpUseless
            do! spaces
            let! list = (many (transitionOrSkipping role)) 
            printfn "%A" list
            return 
                list 
                |> List.filter Option.isSome 
                |> List.map Option.get
                |> Transitions
        }
module Parsing = 
    open parserHelper
    type ScribbleAPI = FSharp.Data.JsonProvider<""" { "code":"Code", "proto":"global protocol", "role":"local role" } """>
    type DiGraph = FSharp.Data.JsonProvider<""" {"result":"value"} """>
    type ScribbleProtocole = FSharp.Data.JsonProvider<""" [ { "currentState":0 , "localRole":"StringLocalRole" , "partner":"StringPartner" , "label":"StringLabel" , "payload":[{"varName":"someZ", "varType":"someType"}] , "assertion":"expression", "type":"EventType" , "nextState":0  } ] """>
                        

    let isCurrentChoice (fsm:ScribbleProtocole.Root []) (index:int) =
        let current = fsm.[index].CurrentState
        let mutable size = 0 
        for elem in fsm do
            if elem.CurrentState = current then
                size <- size + 1
        (size>1)

    let modifyAllChoice (fsm:ScribbleProtocole.Root []) =
        let mutable newArray = [||] 
        for i in 0..(fsm.Length-1) do
            let elem = fsm.[i]
            if elem.Type = "receive" && (isCurrentChoice fsm i) then
                let newElem = ScribbleProtocole.Root(elem.CurrentState,elem.LocalRole,elem.Partner,elem.Label,elem.Payload, "expression","choice",elem.NextState)
                newArray <- Array.append newArray [|newElem|]            
            else
            newArray <- Array.append newArray [|elem|]
        newArray

    let transformPayloadToJson(payload: List<string*string>) = 
        [for elem in payload do
            yield ScribbleProtocole.Payload((fst elem), (snd elem))
        ]
    
    let transformJsonToPayload(payload: List<ScribbleProtocole.Payload>) = 
        [for elem in payload do
            yield (elem.VarName, elem.VarType)
        ]

    let getArrayJson (response:string) json =
        //let s = DiGraph.Parse(response)
        //let s0 = s.Result
        let s0 = response
        match Regex.IsMatch(s0,"java\\.lang\\.NullPointerException") with
        |true ->  None
        |false ->
            let role = ScribbleAPI.Parse(json)
            let test = run (transitions (role.Role) ) s0
            match test with
            | Failure (error,_,_) -> 
                printfn "%s" error
                None
            | Success (res,_,_) ->
                printfn "%s" (res.Stringify())
                let res = ScribbleProtocole.Parse(res.Stringify())
                let newRes = modifyAllChoice res
                let finalRes =
                    [ for tr in newRes do
                        yield
                            {
                                Current     = tr.CurrentState |> Current
                                Role        = tr.LocalRole |> Role
                                Partner     = tr.Partner |> Partner
                                Label       = tr.Label |> Label
                                TrPayload   = tr.Payload |> List.ofArray |> transformJsonToPayload  |> TrPayload
                                Assertion   = tr.Assertion |> genLambdaFromStr |> Assertion
                                EventType   = tr.Type |> EventType
                                Next        = tr.NextState |> Next
                            }  
                    ] |> Transitions
                Some (finalRes.Stringify())
                
    let getFSMJson (json:string) = getArrayJson json