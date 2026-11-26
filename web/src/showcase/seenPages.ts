/** A page without requests that was read before the application: it joins the timeline of the visit once there is one. */
export interface SeenPage {
  name: string;
  at: number;
}
