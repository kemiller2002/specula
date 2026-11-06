namespace Specula.Observer.Cli

module Program =

    let usage =
        """specula-observer: read-only implementation observer (Rehearsal 009 replay)

  scope    --indy-init DIR [--baseline DIR] --out FILE
  collect  --repo GITDIR --scope FILE --indy-init DIR --runs FILE --logs FILE --out FILE
  replay   --scope FILE --timeline FILE --out DIR [--web FILE]
  evaluate --results DIR --ground-truth FILE --out DIR"""

    [<EntryPoint>]
    let main argv =
        let args = Array.toList argv
        try
            match args with
            | "scope" :: rest ->
                let manifest = Scope.build (Common.requiredArg "--indy-init" rest) (Common.arg "--baseline" rest)
                Common.writeJson (Common.requiredArg "--out" rest) manifest
                0
            | "collect" :: rest ->
                Collect.run rest
                0
            | "replay" :: rest ->
                Output.run rest
                0
            | "evaluate" :: rest ->
                Evaluate.run rest
                0
            | _ ->
                eprintfn "%s" usage
                2
        with ex ->
            eprintfn "error: %s" ex.Message
            1
