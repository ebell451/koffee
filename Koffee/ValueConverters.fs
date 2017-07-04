﻿module Koffee.ValueConverters

open System.Windows.Data
open Microsoft.FSharp.Reflection
open Reflection

type UnionText() =
    interface IValueConverter with
        override this.Convert(value, targetType, _, _) =
            value |> GetUnionCaseName |> box

        override this.ConvertBack(value, targetType, _, _) =
            ParseUnionCaseUntyped targetType value |> Option.toObj

type UnionValue() =
    let caseValue index value =
        match FSharpValue.GetUnionFields(value, value.GetType()) with
        | case, _ -> case.GetFields().[index].GetValue(value)

    interface IValueConverter with
        override this.Convert(value, targetType, _, _) =
            value |> caseValue 0 |> box

        override this.ConvertBack(value, targetType, _, _) =
            let fieldTypes (case: UnionCaseInfo) =
                case.GetFields() |> Array.map (fun f -> f.PropertyType)
            let expectedTypes = [|value.GetType()|]
            let case =
                FSharpType.GetUnionCases targetType
                |> Array.filter (fun uc -> fieldTypes uc = expectedTypes)
                |> Array.head
            FSharpValue.MakeUnion(case, [|value|], false)

type OptionValue() =
    interface IValueConverter with
        override this.Convert(value, targetType, _, _) =
            if value = null then value
            else
                let t = value.GetType()
                if t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<_ option>
                then
                    this.GetType().GetMethod("ValueOrDefault").MakeGenericMethod(t.GetGenericArguments().[0])
                        .Invoke(this, [|value|])
                else value

        override this.ConvertBack(value, targetType, _, _) =
            if value = null then None |> box
            else
                let optType =
                    match targetType.ContainsGenericParameters with
                    | true -> targetType.MakeGenericType([|value.GetType()|])
                    | false -> targetType
                optType.GetConstructors().[0].Invoke([|value|])

    member this.ValueOrDefault (opt: 'a option) =
        match opt with
        | Some value -> value
        | None -> Unchecked.defaultof<'a>
