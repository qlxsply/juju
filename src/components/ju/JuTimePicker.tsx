import { TimePicker } from "../date-time/AirDateTimePicker";
import type { TimePickerProps } from "../date-time/types";

export type JuTimePickerProps = TimePickerProps;

export function JuTimePicker(props: JuTimePickerProps) {
    return <TimePicker {...props} />;
}
