import BaseLayouter from 'diagram-js/lib/layout/BaseLayouter';
import { getMid } from 'diagram-js/lib/layout/LayoutUtil';

/**
 * Lays out a flow as a polyline that ends on the outline of its nodes.
 *
 * diagram-js's own layouter returns a line from centre to centre and leaves
 * cropping to whoever calls it. Without cropping, arrow heads end hidden in
 * the middle of the target, and the stored end points are centres, which
 * another tool draws as a line into the shape.
 *
 * Bend points the user placed are kept. The end points are always recomputed
 * from the outline the renderer reports (Renderer#getShapePath), which is why
 * the exchange format stores only the bend points.
 */
export default class Layouter extends BaseLayouter {
  constructor(connectionDocking) {
    super();
    this._connectionDocking = connectionDocking;
  }

  layoutConnection(connection, hints = {}) {
    const source = hints.source || connection.source;
    const target = hints.target || connection.target;

    const current = hints.waypoints || connection.waypoints || [];
    const bends = current.length > 2 ? current.slice(1, -1) : [];

    const waypoints = [
      hints.connectionStart || getMid(source),
      ...bends,
      hints.connectionEnd || getMid(target),
    ];

    if (!source || !target) return waypoints;

    return this._connectionDocking.getCroppedWaypoints({ waypoints, source, target }, source, target);
  }
}

Layouter.$inject = ['connectionDocking'];
