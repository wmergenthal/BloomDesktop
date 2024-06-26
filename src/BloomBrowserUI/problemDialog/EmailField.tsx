import * as React from "react";
import "./ProblemDialog.less";
import TextField from "@mui/material/TextField";
import { useState } from "react";
import { useDebouncedCallback } from "use-debounce";
import { useDrawAttention } from "../react_components/UseDrawAttention";
import { useL10n } from "../react_components/l10nHooks";

//Note: the "isemail" package was not compatible with geckofx 45, so I'm just going with regex
// from https://stackoverflow.com/a/46181/723299
// NB: should handle emails like 用户@例子.广告
const emailPattern = /^(([^<>()\[\]\.,;:\s@\"]+(\.[^<>()\[\]\.,;:\s@\"]+)*)|(\".+\"))@(([^<>()[\]\.,;:\s@\"]+\.)+[^<>()[\]\.,;:\s@\"]{2,})$/i;

export function isValidEmail(email: string): boolean {
    return emailPattern.test(email);
}

export const EmailField: React.FunctionComponent<{
    submitAttempts: number;
    email: string;
    onChange: (email: string) => void;
}> = props => {
    const [emailValid, setEmailValid] = useState(false);

    const debounced = useDebouncedCallback(value => {
        setEmailValid(isValidEmail(value));
    }, 100);
    const debouncedEmailCheck = debounced.callback;

    const localizedEmail = useL10n("Email", "ReportProblemDialog.Email");

    // This is needed in order to get the initial check, when we are loading the stored email address from the api
    React.useEffect(() => {
        debouncedEmailCheck(props.email);
    }, [props.email]);

    const attentionClass = useDrawAttention(
        props.submitAttempts,
        () => emailValid
    );

    return (
        <TextField
            className={"email " + attentionClass}
            variant="outlined"
            label={localizedEmail}
            rows="1"
            InputLabelProps={{
                shrink: true
            }}
            multiline={false}
            aria-label="email"
            error={
                (props.email.length > 0 && !emailValid) ||
                (props.submitAttempts > 0 && !emailValid)
            }
            onChange={event => {
                props.onChange(event.target.value);
                //  debouncedEmailCheck(event.target.value);
            }}
            value={props.email}
        />
    );
};
