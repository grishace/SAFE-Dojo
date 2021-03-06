module App

open Elmish

open Fable
open Fable.Core
open Fable.Core.JsInterop
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Fable.PowerPack
open Fable.Recharts
open Fable.Recharts.Props

open Fulma
open Fulma.FontAwesome

open Shared
open System.Drawing

/// The different elements of the completed report.
type Report =
    { Location : LocationResponse
      Crimes : CrimeResponse array
      Weather : WeatherResponse }

type ServerState = Idle | Loading | ServerError of string

/// The overall data model driving the view.
type Model =
    { Postcode : string
      ValidationError : string option
      ServerState : ServerState
      Report : Report option }
    member __.Empty =
        { Postcode = ""; ValidationError = Some(""); ServerState = ServerState.Idle; Report = None }

/// The different types of messages in the system.
type Msg =
    | GetReport
    | PostcodeChanged of string
    | GotReport of Report
    | ErrorMsg of exn
    | ClearModel

/// The init function is called to start the message pump with an initial view.
let init () = 
    { Postcode = null
      Report = None
      ValidationError = None
      ServerState = Idle }, Cmd.ofMsg (PostcodeChanged "")

[<PassGenerics>]
let getJsonSafe<'T> url record = promise {
        let! res = Fetch.postRecord url record []
        let! response = res.text()
        return ofJson<'T> response
    }

let getResponse postcode = promise {
    let! location = getJsonSafe<LocationResponse> "/api/distance/" { postcode  = postcode }
    let! crimes = Fetch.tryFetchAs<CrimeResponse array> (sprintf "api/crime/%s" postcode) [] |> Promise.map (Result.defaultValue [||])
    let! weather = Fetch.fetchAs<WeatherResponse> (sprintf "api/weather/%s" postcode) []
    
    return { Location = location; Crimes = crimes; Weather = weather } }
 
/// The update function knows how to update the model given a message.
let update msg model =
    match model, msg with
    | { ValidationError = None; Postcode = postcode }, GetReport ->
        { model with ServerState = Loading }, Cmd.ofPromise getResponse postcode GotReport ErrorMsg
    | _, GetReport -> model, Cmd.none
    | _, GotReport response ->
        { model with
            ValidationError = None
            Report = Some response
            ServerState = Idle }, Cmd.none
    | _, PostcodeChanged p ->
        let p = p.Trim().ToUpper()
        { model with
            Postcode = p
            ValidationError = if Validation.validatePostcode p 
                                then None 
                                else match p with _ when p = "" -> Some("") | _ -> Some("Invalid postcode") }, Cmd.none
    | _, ClearModel -> model.Empty, Cmd.none
    | _, ErrorMsg e -> { model with ServerState = ServerError e.Message }, Cmd.none

[<AutoOpen>]
module ViewParts =
    let basicTile title options content =
        Tile.tile options [
            Notification.notification [ Notification.Props [ Style [ Height "100%"; Width "100%" ] ] ]
                (Heading.h2 [] [ str title ] :: content)
        ]
    let childTile title content =
        Tile.child [ ] [
            Notification.notification [ Notification.Props [ Style [ Height "100%"; Width "100%" ] ] ]
                (Heading.h2 [ ] [ str title ] :: content)
        ]
    
    let crimeTile crimes =
        let cleanData = crimes |> Array.map (fun c -> { c with Crime = c.Crime.[0..0].ToUpper() + c.Crime.[1..].Replace('-', ' ') } )
        basicTile "Crime" [ ] [
            barChart
                [ Chart.Data cleanData
                  Chart.Width 600.
                  Chart.Height 500.
                  Chart.Layout Vertical ]
                [ xaxis [ Cartesian.Type "number" ] []
                  yaxis [ Cartesian.Type "category"; Cartesian.DataKey "Crime"; Cartesian.Width 200. ] []
                  bar [ Cartesian.DataKey "Incidents" ] [] ]
        ]

    let getBingMapUrl latLong =
        sprintf "https://www.bing.com/maps/embed?h=400&w=800&cp=%f~%f&lvl=11&typ=s&FORM=MBEDV8" latLong.Latitude latLong.Longitude

    let bingMapTile (latLong:LatLong) =
        basicTile "Map" [ Tile.Size Tile.Is12 ] [
            iframe [
                Style [ Height 410; Width 810 ]
                Src (getBingMapUrl latLong)
            ] [ ]
        ]

    let weatherTile weatherReport =
        childTile "Weather" [
            Level.level [ ] [
                Level.item [ Level.Item.HasTextCentered ] [
                    div [ ] [
                        Level.heading [ ] [
                            Image.image [ Image.Is128x128 ] [
                                img [ Src(sprintf "https://www.metaweather.com/static/img/weather/%s.svg" weatherReport.WeatherType.Abbreviation) ]
                            ]
                        ]
                        Level.title [ ] [
                            Heading.h3 [ Heading.Is4; Heading.Props [ Style [ Width "100%" ] ] ] [
                                str (weatherReport.AverageTemperature.ToString("F1") + " \u00B0C")
                            ]
                        ]
                    ]
                ]
            ]
        ]
    let locationTile model =
        childTile "Location" [
            div [ ] [
                Heading.h3 [ ] [ str model.Location.Location.Town ]
                Heading.h4 [ ] [ str model.Location.Location.Region ]
                Heading.h4 [ ] [ sprintf "%.1fKM to London" model.Location.DistanceToLondon |> str ]
            ]
        ]
             

/// The view function knows how to render the UI given a model, as well as to dispatch new messages based on user actions.
let view model dispatch =
    div [] [
        Navbar.navbar [ Navbar.Color IsPrimary ] [
            Navbar.Item.div [] [
                Heading.h1 [] [ str "SAFE Dojo (with custom styles)" ] ]
            ]
        
        Container.container [] [
            yield 
                Field.div [] [
                    Label.label [] [ str "Postcode" ]
                    Control.div [ Control.HasIconLeft; Control.HasIconRight ] [
                        Input.text
                            [ Input.Placeholder "Ex: EC2A 4NE"
                              Input.Value model.Postcode
                              Input.Color (
                                  match model.ValidationError with
                                  | Some(p) when p <> "" -> Color.IsDanger
                                  | None -> Color.IsSuccess
                                  | _ -> Color.NoColor)
                              Input.Props [ OnChange (fun ev -> dispatch (PostcodeChanged !!ev.target?value)); onKeyDown KeyCode.enter (fun _ -> dispatch GetReport) ] ]
                        Icon.faIcon [ Icon.Size IsSmall; Icon.IsLeft ] [ Fa.icon Fa.I.Building ]
                        (match model with
                         | { ValidationError = Some p } when p <> "" -> Icon.faIcon [ Icon.Size IsSmall; Icon.IsRight ] [ Fa.icon Fa.I.Exclamation ]
                         | { ValidationError = None } -> Icon.faIcon [ Icon.Size IsSmall; Icon.IsRight ] [ Fa.icon Fa.I.Check ]
                         | _ -> Icon.faIcon [] [])
                    ]
                    Help.help
                       [ Help.Color (if model.ValidationError.IsNone then IsSuccess else IsDanger) ]
                       [ str (model.ValidationError |> Option.defaultValue "") ]
                ]
            yield
                Field.div [ Field.IsGrouped ] [
                    Level.level [] [
                        Level.left [] [
                            Level.item [] [
                                Button.button
                                    [ Button.IsFullWidth
                                      Button.Color IsPrimary
                                      Button.OnClick (fun _ -> dispatch GetReport)
                                      Button.Disabled (model.ValidationError.IsSome)
                                      Button.IsLoading (model.ServerState = ServerState.Loading) ]
                                    [ str "Submit" ]
                                    
                                // TODO: what is the proper way to do this? Button.list [] [] ?
                                div [ DangerouslySetInnerHTML { __html = "&nbsp;"} ] []
                                Button.button
                                    [ Button.IsFullWidth
                                      Button.Color IsPrimary
                                      Button.OnClick (fun _ -> dispatch ClearModel ) ]
                                    [ str "Clear" ] ] ] ]
                ]

            match model with
            | { Report = None; ServerState = (Idle | Loading) } -> ()
            | { ServerState = ServerError error } ->
                yield
                    Field.div [] [
                        Tag.list [ Tag.List.HasAddons; Tag.List.IsCentered ] [
                            Tag.tag [ Tag.Color Color.IsDanger; Tag.Size IsMedium ] [
                                str error
                            ]
                        ]
                    ]
            | { Report = Some model } ->
                yield
                    Tile.ancestor [ ] [
                        Tile.parent [ Tile.Size Tile.Is12 ] [
                            bingMapTile model.Location.Location.LatLong
                        ]
                    ]
                yield
                    Tile.ancestor [ ] [
                        Tile.parent [ Tile.IsVertical; Tile.Size Tile.Is4 ] [ 
                            locationTile model
                            weatherTile model.Weather
                        ]
                        Tile.parent [ Tile.Size Tile.Is8 ] [
                            crimeTile model.Crimes
                        ]                   
                  ]        
        ]

        br [ ]

        Footer.footer [] [
            Content.content
                [ Content.CustomClass TextAlignment.Classes.HasTextCentered ]
                [ safeComponents ]
        ]
    ]
