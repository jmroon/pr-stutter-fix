"""Offline design model, NOT a game adapter or a shipped correction.

Coordinates must already be in one aligned XY space. Native adapters must prove
that correspondence, reproduce camera mapping, and own visual-only writes.
This model removes spatial rounding only; it cannot recover discarded time.
"""
import math


def fractional_residual(start, destination, timer, duration, observed):
    """Admit a verified linear segment, independent of tile length or speed.

    A tuple is one XY position. None means incompatible evidence, not permission
    to move the object. An already fractional position needs zero correction.
    Terminal/stationary/teleport handling belongs to the adapter; no extrapolation.
    """
    values = (*start, *destination, timer, duration, *observed)
    if not all(math.isfinite(v) for v in values) or duration <= 0 or not 0 <= timer <= duration:
        return None
    ideal = tuple(a + (b - a) * (timer / duration) for a, b in zip(start, destination))
    if all(abs(a - b) <= 1e-5 for a, b in zip(ideal, observed)):
        return (0.0, 0.0)
    if any(abs(round(a) - b) > 1e-5 for a, b in zip(ideal, observed)):
        return None
    return tuple(a - b for a, b in zip(ideal, observed))


def map_axis(value, extent, offset=0.0, loop=False, clamp=True):
    """Inspected base-map clamp: clamp input about zero, THEN add offset.

    extent = max(0, map cells * 16 - native view size) / 2.
    This is NOT the complete CameraFollowing/visual/minimap/parallax mapping.
    Looping bypasses this clamp; native tile-window wrapping remains separate.
    """
    if not all(math.isfinite(v) for v in (value, extent, offset)) or extent < 0:
        raise ValueError('Invalid map axis')
    return (max(-extent, min(extent, value)) if clamp and not loop else value) + offset


def camera_residual(rounded_source, source_residual, mapping):
    """Evaluate both poses through the SAME mapping, including partial clamping."""
    return mapping(rounded_source + source_residual) - mapping(rounded_source)


def screen_residual(entity_residual, camera_residual_value):
    """For a unit-scale, unrotated view; adapters handle other native mappings."""
    return entity_residual - camera_residual_value
