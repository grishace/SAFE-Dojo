module Api

open DataAccess
open FSharp.Data.UnitSystems.SI.UnitNames
open Giraffe
open Microsoft.AspNetCore.Http
open Saturn
open Shared

let private london = { Latitude = 51.5074; Longitude = 0.1278 }
let invalidPostcode next (ctx:HttpContext) =
    ctx.SetStatusCode 400
    text "Invalid postcode" next ctx

let getDistanceFromLondon : HttpHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            // Get post data!
            let! r = ctx.BindModelAsync<PostcodeRequest>()

            if Validation.validatePostcode r.postcode then
                let! location = getLocation r.postcode
                let distanceToLondon = getDistanceBetweenPositions location.LatLong london
                return! json { Postcode = r.postcode; Location = location; DistanceToLondon = (distanceToLondon / 1000.<meter>) } next ctx
            else return! invalidPostcode next ctx 
        }

let getCrimeReport postcode next ctx = task {
    if Validation.validatePostcode postcode then
        let! location = getLocation postcode
        let! reports = Crime.getCrimesNearPosition location.LatLong
        let crimes =
            reports
            |> Array.countBy(fun r -> r.Category)
            |> Array.sortByDescending snd
            |> Array.map(fun (k, c) -> { Crime = k; Incidents = c })
        return! json crimes next ctx
    else return! invalidPostcode next ctx }

let private asWeatherResponse (weather:DataAccess.Weather.MetaWeatherLocation.Root) =
    { WeatherType =
        weather.ConsolidatedWeather
        |> Array.countBy(fun w -> w.WeatherStateName)
        |> Array.maxBy snd
        |> fst
        |> WeatherType.Parse
      AverageTemperature = weather.ConsolidatedWeather |> Array.averageBy(fun r -> float r.TheTemp) }

let getWeather postcode next ctx = task {  
    let! location = getLocation postcode
    let! weather = DataAccess.Weather.getWeatherForPosition location.LatLong
    return! json (weather |> asWeatherResponse) next ctx }

let apiRouter = scope {
    pipe_through (pipeline { set_header "x-pipeline-type" "Api" })

    post "/distance/" getDistanceFromLondon

    getf "/crime/%s" getCrimeReport

    getf "/weather/%s" getWeather

    }
